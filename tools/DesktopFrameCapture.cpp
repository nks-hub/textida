// Build in an x64 Visual Studio developer prompt:
// cl /nologo /W4 /EHsc /MT /O2 /std:c++17 tools\DesktopFrameCapture.cpp /link d3d11.lib dxgi.lib user32.lib
#define NOMINMAX
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d11.h>
#include <dxgi1_2.h>
#include <wrl/client.h>
#include <algorithm>
#include <array>
#include <cerrno>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <iomanip>
#include <sstream>
#include <stdexcept>
#include <string>

using Microsoft::WRL::ComPtr;

namespace {
struct Handle {
    HANDLE value;
    explicit Handle(HANDLE handle) : value(handle) {}
    Handle(const Handle&) = delete;
    Handle& operator=(const Handle&) = delete;
    ~Handle() { if (value && value != INVALID_HANDLE_VALUE) CloseHandle(value); }
};

struct FrameLease {
    IDXGIOutputDuplication* capture;
    ~FrameLease() { capture->ReleaseFrame(); }
};

void Check(HRESULT result, const char* operation) {
    if (SUCCEEDED(result)) return;
    char message[160];
    std::snprintf(message, sizeof(message), "%s failed: %08lx", operation, static_cast<unsigned long>(result));
    throw std::runtime_error(message);
}

long long Counter() {
    LARGE_INTEGER value{};
    if (!QueryPerformanceCounter(&value)) throw std::runtime_error("QueryPerformanceCounter failed");
    return value.QuadPart;
}

long Number(const wchar_t* text) {
    wchar_t* end = nullptr;
    errno = 0;
    long value = std::wcstol(text, &end, 10);
    if (errno || end == text || *end) throw std::invalid_argument("Invalid numeric argument");
    return value;
}

void Write(HANDLE file, const std::string& text) {
    DWORD written = 0;
    if (!WriteFile(file, text.data(), static_cast<DWORD>(text.size()), &written, nullptr) || written != text.size())
        throw std::runtime_error("Writing capture CSV failed");
}

bool Contains(const RECT& bounds, POINT point) {
    return point.x >= bounds.left && point.x < bounds.right && point.y >= bounds.top && point.y < bounds.bottom;
}

DWORD Owner(HWND window) {
    DWORD pid = 0;
    GetWindowThreadProcessId(GetAncestor(window, GA_ROOT), &pid);
    return pid;
}

std::uint32_t Pixel(const BYTE* bytes, UINT pitch, POINT point) {
    return reinterpret_cast<const std::uint32_t*>(bytes + static_cast<size_t>(point.y) * pitch)[point.x] & 0xFFFFFF;
}

int Capture(DWORD pid, POINT first, POINT second, int seconds, const wchar_t* path) {
    Handle process(OpenProcess(SYNCHRONIZE, FALSE, pid));
    if (!process.value) throw std::runtime_error("Opening target process failed");
    ComPtr<IDXGIFactory1> factory;
    Check(CreateDXGIFactory1(IID_PPV_ARGS(&factory)), "CreateDXGIFactory1");
    ComPtr<IDXGIOutputDuplication> capture;
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> context;
    DXGI_OUTPUT_DESC outputBounds{};
    for (UINT a = 0; a < 16 && !capture; ++a) {
        ComPtr<IDXGIAdapter1> adapter;
        HRESULT hr = factory->EnumAdapters1(a, &adapter);
        if (hr == DXGI_ERROR_NOT_FOUND) break;
        Check(hr, "EnumAdapters1");
        for (UINT o = 0; o < 16; ++o) {
            ComPtr<IDXGIOutput> output;
            hr = adapter->EnumOutputs(o, &output);
            if (hr == DXGI_ERROR_NOT_FOUND) break;
            Check(hr, "EnumOutputs");
            Check(output->GetDesc(&outputBounds), "GetDesc(output)");
            if (!Contains(outputBounds.DesktopCoordinates, first) || !Contains(outputBounds.DesktopCoordinates, second)) continue;
            device.Reset(); context.Reset();
            hr = D3D11CreateDevice(adapter.Get(), D3D_DRIVER_TYPE_UNKNOWN, nullptr, D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                nullptr, 0, D3D11_SDK_VERSION, &device, nullptr, &context);
            if (FAILED(hr)) continue;
            ComPtr<IDXGIOutput1> output1;
            Check(output.As(&output1), "QueryInterface(output1)");
            hr = output1->DuplicateOutput(device.Get(), &capture);
            std::printf("adapter=%u output=%u duplicate=%08lx\n", a, o, static_cast<unsigned long>(hr));
            if (SUCCEEDED(hr)) break;
        }
    }
    if (!capture) throw std::runtime_error("No desktop duplication output supports both sample points");
    DXGI_OUTDUPL_DESC description{};
    capture->GetDesc(&description);
    if (description.Rotation != DXGI_MODE_ROTATION_IDENTITY && description.Rotation != DXGI_MODE_ROTATION_UNSPECIFIED)
        throw std::runtime_error("Rotated capture outputs are not supported");
    if (description.ModeDesc.Format != DXGI_FORMAT_B8G8R8A8_UNORM) throw std::runtime_error("Unexpected desktop format");
    POINT localFirst{first.x - outputBounds.DesktopCoordinates.left, first.y - outputBounds.DesktopCoordinates.top};
    POINT localSecond{second.x - outputBounds.DesktopCoordinates.left, second.y - outputBounds.DesktopCoordinates.top};
    const LONG left = std::min(localFirst.x, localSecond.x), top = std::min(localFirst.y, localSecond.y);
    const UINT width = static_cast<UINT>(std::abs(localFirst.x - localSecond.x) + 1);
    const UINT height = static_cast<UINT>(std::abs(localFirst.y - localSecond.y) + 1);
    if (width > 256 || height > 256) throw std::runtime_error("Sample points must be within 256 pixels of each other");
    ComPtr<ID3D11Texture2D> staging;
    if (!description.DesktopImageInSystemMemory) {
        D3D11_TEXTURE2D_DESC target{};
        target.Width = width; target.Height = height; target.MipLevels = 1; target.ArraySize = 1;
        target.Format = DXGI_FORMAT_B8G8R8A8_UNORM; target.SampleDesc.Count = 1;
        target.Usage = D3D11_USAGE_STAGING; target.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
        Check(device->CreateTexture2D(&target, nullptr, &staging), "CreateTexture2D(staging)");
    }
    Handle file(CreateFileW(path, GENERIC_WRITE, FILE_SHARE_READ, nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr));
    if (file.value == INVALID_HANDLE_VALUE) throw std::runtime_error("Creating capture CSV failed; the file must not already exist");
    Write(file.value, "present_qpc,mouse_qpc,received_qpc,read_start_qpc,read_end_qpc,first_rgb,second_rgb,window_hwnd,accumulated_frames,status\n");
    LARGE_INTEGER frequency{};
    if (!QueryPerformanceFrequency(&frequency)) throw std::runtime_error("QueryPerformanceFrequency failed");
    DWORD session = 0;
    ProcessIdToSessionId(GetCurrentProcessId(), &session);
    const long long started = Counter(), deadline = started + seconds * frequency.QuadPart;
    std::printf("ready_qpc=%lld qpc_frequency=%lld session=%lu pid=%lu system_memory=%d size=%ux%u\n",
        started, frequency.QuadPart, session, pid, description.DesktopImageInSystemMemory,
        description.ModeDesc.Width, description.ModeDesc.Height);
    std::fflush(stdout);
    unsigned frames = 0, pointerOnly = 0, owned = 0;
    while (Counter() < deadline && WaitForSingleObject(process.value, 0) == WAIT_TIMEOUT) {
        DXGI_OUTDUPL_FRAME_INFO info{};
        ComPtr<IDXGIResource> image;
        HRESULT hr = capture->AcquireNextFrame(100, &info, &image);
        if (hr == DXGI_ERROR_WAIT_TIMEOUT) continue;
        Check(hr, "AcquireNextFrame");
        std::string row;
        {
            FrameLease lease{capture.Get()};
            ++frames;
            if (!info.LastPresentTime.QuadPart) ++pointerOnly;
            const long long received = Counter();
            if (info.LastPresentTime.QuadPart > received) throw std::runtime_error("Desktop frame timestamp is in the future");
            const HWND window = GetAncestor(GetForegroundWindow(), GA_ROOT);
            const bool matches = Owner(window) == pid && Owner(WindowFromPoint(first)) == pid && Owner(WindowFromPoint(second)) == pid;
            std::array<std::uint32_t, 2> colors{};
            long long begin = 0, end = 0;
            if (matches && !info.ProtectedContentMaskedOut && info.LastPresentTime.QuadPart) {
                ++owned;
                begin = Counter();
                if (description.DesktopImageInSystemMemory) {
                    DXGI_MAPPED_RECT mapped{};
                    Check(capture->MapDesktopSurface(&mapped), "MapDesktopSurface");
                    if (mapped.Pitch < static_cast<INT>(description.ModeDesc.Width * 4)) {
                        capture->UnMapDesktopSurface();
                        throw std::runtime_error("Invalid desktop pitch");
                    }
                    colors = {Pixel(mapped.pBits, mapped.Pitch, localFirst), Pixel(mapped.pBits, mapped.Pitch, localSecond)};
                    Check(capture->UnMapDesktopSurface(), "UnMapDesktopSurface");
                } else {
                    ComPtr<ID3D11Texture2D> texture;
                    Check(image.As(&texture), "QueryInterface(texture)");
                    D3D11_BOX box{static_cast<UINT>(left), static_cast<UINT>(top), 0,
                        static_cast<UINT>(left) + width, static_cast<UINT>(top) + height, 1};
                    context->CopySubresourceRegion(staging.Get(), 0, 0, 0, 0, texture.Get(), 0, &box);
                    D3D11_MAPPED_SUBRESOURCE mapped{};
                    Check(context->Map(staging.Get(), 0, D3D11_MAP_READ, 0, &mapped), "Map(staging)");
                    colors = {Pixel(static_cast<const BYTE*>(mapped.pData), mapped.RowPitch, {localFirst.x - left, localFirst.y - top}),
                        Pixel(static_cast<const BYTE*>(mapped.pData), mapped.RowPitch, {localSecond.x - left, localSecond.y - top})};
                    context->Unmap(staging.Get(), 0);
                }
                end = Counter();
            }
            std::ostringstream line;
            line << info.LastPresentTime.QuadPart << ',' << info.LastMouseUpdateTime.QuadPart << ',' << received << ',' << begin << ',' << end << ',';
            if (end) line << std::hex << std::uppercase << std::setfill('0') << std::setw(6) << colors[0] << ',' << std::setw(6) << colors[1] << std::dec;
            else line << ',';
            line << ',' << reinterpret_cast<std::uintptr_t>(window) << ',' << info.AccumulatedFrames << ','
                << (!matches ? "not-owned" : info.ProtectedContentMaskedOut ? "masked" : !info.LastPresentTime.QuadPart ? "no-image-time" : "captured") << '\n';
            row = line.str();
        }
        Write(file.value, row);
    }
    std::printf("frames=%u pointer_only=%u owned=%u finished_qpc=%lld target_exited=%d\n",
        frames, pointerOnly, owned, Counter(), WaitForSingleObject(process.value, 0) == WAIT_OBJECT_0);
    return 0;
}
}

int wmain(int argc, wchar_t* argv[]) {
    try {
        if (!SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)
            && GetAwarenessFromDpiAwarenessContext(GetThreadDpiAwarenessContext()) != DPI_AWARENESS_PER_MONITOR_AWARE)
            throw std::runtime_error("Per-monitor DPI awareness is required");
        if (argc != 8) {
            std::fputs("Usage: DesktopFrameCapture <pid> <x1> <y1> <x2> <y2> <seconds> <new-csv>\n", stderr);
            return 2;
        }
        const long pid = Number(argv[1]), seconds = Number(argv[6]);
        if (pid < 1 || seconds < 1 || seconds > 1200) throw std::invalid_argument("PID must be positive; seconds must be 1..1200");
        return Capture(static_cast<DWORD>(pid), {Number(argv[2]), Number(argv[3])}, {Number(argv[4]), Number(argv[5])}, seconds, argv[7]);
    } catch (const std::exception& error) {
        std::fprintf(stderr, "%s\n", error.what());
        return 1;
    }
}
