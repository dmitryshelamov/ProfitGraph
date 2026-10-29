const labels: Record<string, string> = {
  SmaCrossover: 'SMA Crossover',
  Ladder: 'Ladder',
  LadderV2: 'Ladder V2',
  LadderProfitFox: 'Ladder (как в Profit Fox)',
  LadderSma: 'Ladder SMA',
  CapitulationDip: 'Откуп капитуляции',
  VolumeMomentum: 'Импульс на объёме',
  LadderPlus: 'Ladder+',
  DcaProfitFox: 'DCA (как в Profit Fox)',
  Grid: 'Сетка',
};

/** The template a strategy was created from; a strategy without a template marker is the user's own. */
export function strategyTemplateLabel(kind: string | null | undefined): string {
  return (kind && labels[kind]) || 'Своя стратегия';
}
