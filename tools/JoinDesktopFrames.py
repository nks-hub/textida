"""Match state-bound app markers to DXGI desktop image updates, not to CPU read completion."""
import argparse
from collections import Counter, defaultdict
import csv
import json
import math
from pathlib import Path
import re


def load_csv(path):
    with path.open(encoding="utf-8-sig", newline="") as file:
        return list(csv.DictReader(file))


def join_frames(records, samples, frames, capture_meta):
    if not samples:
        raise ValueError("The app samples CSV is empty")
    runs = {row["run_id"] for row in samples}
    if len(runs) != 1:
        raise ValueError("Use samples from a single app run")
    metadata = [r for r in records if r["t"] == "samples" and r["runId"] in runs]
    windows = [r for r in records if r["t"] == "window"]
    probes = [r for r in records if r["t"] == "desktop-probe"]
    if len(metadata) != 1 or len(windows) != 1 or len(probes) != 1:
        raise ValueError("The JSONL must contain one measurement run, window and desktop probe")
    if probes[0].get("markerBinding") != "draw-state":
        raise ValueError("Frame markers must be bound to the rendered document state")
    if any(r["t"] == "error" for r in records) or not any(r["t"] == "done" for r in records):
        raise ValueError("The app measurement did not complete")
    frequency = int(metadata[0]["qpcFrequency"])
    capture_frequency = re.search(r"qpc_frequency=(\d+)", capture_meta)
    capture_session = re.search(r"session=(\d+)", capture_meta)
    if frequency <= 0 or not capture_frequency or int(capture_frequency[1]) != frequency:
        raise ValueError("The app and capture counter frequencies differ")
    if not capture_session or int(capture_session[1]) != int(probes[0]["session"]) or int(capture_session[1]) == 0:
        raise ValueError("The capture must use the app's interactive session")
    if len({row["desktop_marker"] for row in samples}) != len(samples):
        raise ValueError("Frame marker codes are missing or repeated")
    expected = Counter()
    for record in records:
        if record["t"] == "json-open":
            expected["json-open"] += 1
        elif record["t"] == "series":
            expected[record["name"]] += int(record["n"])
        elif record["t"] == "osInput":
            expected["osInput"] += int(record["delivered"])
    if Counter(row["scenario"] for row in samples) != expected:
        raise ValueError("The sample counts differ from the completed JSONL scenarios")
    if "json-open" in expected and probes[0].get("openMarkerArmedBeforeDraw") is not True:
        raise ValueError("The opening marker was not armed before the first draw")
    sequences = defaultdict(list)
    previous_start = 0
    for sample in samples:
        sequences[sample["scenario"]].append(int(sample["sample"]))
        start, elapsed = int(sample["start_qpc"]), float(sample["ui_ms"])
        if start <= previous_start or not math.isfinite(elapsed) or elapsed < 0:
            raise ValueError("Invalid app sample timestamps")
        if sample["scenario"] == "osInput" and (sample["sent"], sample["delivered"]) != ("true", "true"):
            raise ValueError("System input was not sent and delivered")
        previous_start = start
    if any(indices != list(range(len(indices))) for indices in sequences.values()):
        raise ValueError("App sample indices are missing or reordered")

    window = int(windows[0]["hwnd"])
    images = defaultdict(list)
    for frame in frames:
        if frame["status"] != "captured" or int(frame["window_hwnd"]) != window:
            continue
        first, second = int(frame["first_rgb"], 16), int(frame["second_rgb"], 16)
        if first ^ second != 0xFFFFFF:
            continue
        times = [int(frame[key]) for key in ["present_qpc", "received_qpc", "read_start_qpc", "read_end_qpc"]]
        if times[0] <= 0 or times != sorted(times):
            raise ValueError("Invalid desktop capture timestamps")
        images[first].append(frame)

    joined = []
    for sample in samples:
        if int(sample["qpc_frequency"]) != frequency:
            raise ValueError("A sample uses a different counter frequency")
        if not sample["ui_ms"] or sample["desktop_status"] not in ("seen", "external"):
            raise ValueError("The app sample has no completed draw or has an invalid desktop state")
        start = int(sample["start_qpc"])
        drawn = start + float(sample["ui_ms"]) * frequency / 1000
        matches = images.get(int(sample["desktop_marker"], 16), [])
        row = dict(sample)
        row.update(present_qpc="", present_ms="", capture_read_ms="", capture_delivery_ms="", accumulated_frames="", presentation_status="missing")
        if matches:
            frame = min(matches, key=lambda f: int(f["present_qpc"]))
            when = int(frame["present_qpc"])
            if when < drawn - 1:
                row["presentation_status"] = "before-draw"
            else:
                # Draw lists can reach the desktop after the next CPU action has started.
                # The marker is bound to the same immutable drawing as that action's content.
                row.update(present_qpc=str(when), present_ms=str((when - start) * 1000 / frequency),
                    capture_read_ms=str((int(frame["read_end_qpc"]) - int(frame["read_start_qpc"])) * 1000 / frequency),
                    capture_delivery_ms=str((int(frame["received_qpc"]) - when) * 1000 / frequency),
                    accumulated_frames=frame["accumulated_frames"], presentation_status="matched")
        joined.append(row)
    return joined


def summarize(rows):
    result = []
    for name in dict.fromkeys(row["scenario"] for row in rows):
        group = [row for row in rows if row["scenario"] == name]
        matched = [row for row in group if row["presentation_status"] == "matched"]
        summary = {"scenario": name, "n": len(group), "matched": len(matched)}
        if matched:
            for column in ["present_ms", "capture_read_ms", "capture_delivery_ms"]:
                values = sorted(float(row[column]) for row in matched)
                summary[column] = {label: values[math.ceil(q * len(values)) - 1]
                    for label, q in [("p50", .5), ("p95", .95), ("p99", .99), ("max", 1)]}
            summary["coalesced"] = sum(int(row["accumulated_frames"]) > 1 for row in matched)
        result.append(summary)
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("app_jsonl", type=Path)
    parser.add_argument("app_samples", type=Path)
    parser.add_argument("capture_csv", type=Path)
    parser.add_argument("output_csv", type=Path, help="New output file; existing files are preserved")
    parser.add_argument("--app-exit", type=Path, required=True)
    parser.add_argument("--capture-exit", type=Path, required=True)
    parser.add_argument("--capture-meta", type=Path, required=True)
    args = parser.parse_args()
    try:
        for path in [args.app_exit, args.capture_exit]:
            if path.read_text(encoding="utf-8-sig").strip() != "0":
                raise ValueError("Both process exit codes must be present and zero")
        records = [json.loads(line) for line in args.app_jsonl.read_text(encoding="utf-8-sig").splitlines() if line]
        rows = join_frames(records, load_csv(args.app_samples), load_csv(args.capture_csv), args.capture_meta.read_text(encoding="utf-8-sig"))
        with args.output_csv.open("x", encoding="utf-8", newline="") as file:
            writer = csv.DictWriter(file, fieldnames=list(rows[0]))
            writer.writeheader()
            writer.writerows(rows)
        for summary in summarize(rows):
            print(json.dumps(summary))
        complete = all(row["presentation_status"] == "matched" for row in rows)
        print(json.dumps({"all_markers_matched": complete, "samples": len(rows),
            "method": "DXGI LastPresentTime of the first captured image with the state-bound marker",
            "scope": "guest desktop image update; not physical monitor or RDP transport latency"}))
        return 0 if complete else 1
    except (OSError, ValueError, KeyError) as error:
        parser.error(str(error))


if __name__ == "__main__":
    raise SystemExit(main())
