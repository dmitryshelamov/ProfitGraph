using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Scanner.Contracts;

namespace ProfitGraph.Application.Scanner.UpdateScanPreset;

public sealed record UpdateScanPresetCommand(Guid UserId, Guid PresetId, ScanPresetDefinitionInternal Definition) : ICommand<ScanPresetInternal>;