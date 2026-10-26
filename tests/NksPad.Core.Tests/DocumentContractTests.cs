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
