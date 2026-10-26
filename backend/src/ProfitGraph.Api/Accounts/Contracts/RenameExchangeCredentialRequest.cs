using System.ComponentModel.DataAnnotations;

namespace ProfitGraph.Api.Accounts.Contracts;

public sealed record RenameExchangeCredentialRequest([Required] string Label);