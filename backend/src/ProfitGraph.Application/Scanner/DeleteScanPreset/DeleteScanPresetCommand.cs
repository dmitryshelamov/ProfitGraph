using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Scanner.DeleteScanPreset;

public sealed record DeleteScanPresetCommand(Guid UserId, Guid PresetId) : ICommand;