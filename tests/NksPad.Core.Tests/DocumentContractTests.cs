using NksPad.Core.Documents;
using NksPad.Document.Contract;

namespace NksPad.Core.Tests;

public class ReferenceContractTests : DocumentContractSuite
{
    protected override IBackendFactory Factory => ReferenceBackendFactory.Instance;
}

public class PagedContractTests : DocumentContractSuite
{
    protected override IBackendFactory Factory => PagedBackendFactory.Instance;
}

public class ReferenceByteFidelityGate : ByteFidelityGateSuite
{
    protected override IBackendFactory Factory => ReferenceBackendFactory.Instance;
}

/// <summary>UTF-16/32 are P3.1.2: the gate expects a clear NotSupported there and the full gate for the byte-oriented encodings.</summary>
public class PagedByteFidelityGate : ByteFidelityGateSuite
{
    protected override IBackendFactory Factory => PagedBackendFactory.Instance;
}

public class ReferenceEncodingOperations : DocumentEncodingSuite
{
    protected override IBackendFactory Factory => ReferenceBackendFactory.Instance;
}

public class PagedEncodingOperations : DocumentEncodingSuite
{
    protected override IBackendFactory Factory => PagedBackendFactory.Instance;
}
