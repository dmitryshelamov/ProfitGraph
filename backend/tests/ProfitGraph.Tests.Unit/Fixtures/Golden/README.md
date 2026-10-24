# Golden replay fixtures

Each `<template>.<dataset>.txt` file is the canonical replay result of one scenario from
`Engine/Golden/GoldenReplay.cs`, recorded with the archived Profit Fox Designer engine at `mvp@f26ab20d`.
`GoldenReplayTests` replays the same scenario with ProfitGraph and requires an identical result.

To re-record after a deliberate behavior change, run the scenarios with the new engine, review every changed line,
and explain the change in the pull request. The archive recording used a throwaway console project that referenced
`ProfitFox.Designer.Domain` and ran the same `GoldenReplay` source with the archive's namespaces.
