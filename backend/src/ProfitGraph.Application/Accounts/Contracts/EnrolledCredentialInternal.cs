namespace ProfitGraph.Application.Accounts.Contracts;

/// <summary>
///     The credential an enrollment produced: its label, the four characters its owner may see and its fields in plain text,
///     which only the core's credential store encrypts.
/// </summary>
public sealed record EnrolledCredentialInternal(string Label, string PublicHint, IReadOnlyDictionary<string, string> Fields)
{
    public override string ToString()
    {
        return $"EnrolledCredentialInternal {{ Label = {Label}, Redacted }}";
    }
}