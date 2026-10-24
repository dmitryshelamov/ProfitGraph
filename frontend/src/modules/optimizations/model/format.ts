const methods: Record<string, string> = {
  grid: 'Сетка',
  random: 'Случайный поиск',
  coarse_to_fine: 'От грубой сетки к точной',
  tpe: 'TPE',
};

const objectives: Record<string, string> = {
  in_sample_return_percent: 'Доходность, %',
  in_sample_total_pnl: 'Прибыль',
  in_sample_return_to_drawdown: 'Доходность к просадке',
};

const trialStatuses: Record<string, string> = {
  completed: 'Выполнено',
  rejected_by_constraint: 'Вне ограничений',
  rejected_by_template: 'Отклонено шаблоном',
  failed: 'Ошибка',
};

const stages: Record<string, string> = {
  loading_history: 'Подготовка истории',
  searching: 'Поиск',
  starting_finalists: 'Финалисты запущены',
};

const windowStatuses: Record<string, string> = {
  completed: 'Проверено',
  no_winner: 'Нет победителя',
  test_failed: 'Ошибка проверки',
};

export function methodLabel(method: string): string {
  return methods[method] ?? method;
}

export function objectiveLabel(objective: string): string {
  return objectives[objective] ?? objective;
}

export function trialStatusLabel(status: string): string {
  return trialStatuses[status] ?? status;
}

export function stageLabel(stage: string | null | undefined): string {
  return stage ? (stages[stage] ?? stage) : 'В очереди';
}

export function windowStatusLabel(status: string): string {
  return windowStatuses[status] ?? status;
}
