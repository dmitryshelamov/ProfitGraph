using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Scanner.Contracts;

namespace ProfitGraph.Application.Scanner.CreateScanPreset;

public sealed record CreateScanPresetCommand(Guid UserId, string Venue, ScanPresetDefinitionInternal Definition) : ICommand<ScanPresetInternal>;