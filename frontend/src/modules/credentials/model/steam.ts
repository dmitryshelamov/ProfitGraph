/** Why a Steam authenticator link stopped or a step was refused, by the server's problem code. */
export const steamEnrollmentProblems: Readonly<Record<string, string>> = {
  invalid_fields: 'Заполните поля этого шага.',
  invalid_login: 'Steam не принял имя аккаунта или пароль.',
  invalid_code: 'Steam не принял код. Введите его ещё раз.',
  authenticator_exists:
    'У аккаунта уже есть мобильный аутентификатор Steam Guard. Импортируйте его на сервере командой steam-import.',
  phone_required:
    'Steam требует подтверждённый номер телефона. Привяжите телефон в Steam и начните заново.',
  account_exists: 'Этот аккаунт Steam уже сохранён.',
  enrollment_expired: 'Привязка устарела через 10 минут без действий. Начните заново.',
  enrollment_unknown:
    'Steam не подтвердил результат. Проверьте аутентификатор в Steam; повторно запрос не отправлялся.',
  activation_pending: 'Steam просит ещё один код. Активируйте снова.',
  activation_unsupported: 'Steam предлагает подтверждение, которое здесь не поддерживается.',
  enrollment_refused: 'Steam отказал в привязке аутентификатора.',
  enrollment_busy: 'Этот шаг уже выполняется.',
  enrollment_not_found: 'Привязка не найдена. Начните заново.',
  unexpected_step: 'Привязка уже на другом шаге.',
  steam_unavailable: 'Steam сейчас недоступен. Попробуйте позже.',
  steam_unreadable: 'Ответ Steam не удалось прочитать.',
  steam_refused: 'Steam отклонил запрос.',
};

/** A Steam account's Guard state as the owner reads it. */
export const steamGuardStatuses: Readonly<Record<string, string>> = {
  Checking: 'Доступ к Steam Market проверяется; боты не торгуют до успешной проверки.',
  PendingActivation: 'Аутентификатор ждёт активации.',
  CoolingDown: 'Новый аутентификатор выдерживается; Steam пока задерживает продажи.',
  Ready: 'Готов к торговле.',
  Restricted: 'Steam ограничил аккаунт; боты не торгуют.',
};

/** Why a venue account's protection holds its bots back, by reason code. */
export const protectionReasons: Readonly<Record<string, string>> = {
  Unauthorized: 'Steam несколько раз не принял сессию аккаунта.',
  Forbidden: 'Steam несколько раз запретил запросы аккаунта.',
  RateLimited: 'Steam несколько раз ограничил частоту запросов.',
  Captcha: 'Steam потребовал CAPTCHA.',
  Restricted: 'Steam ограничил торговлю аккаунта.',
  InvalidResponse: 'Steam несколько раз ответил нечитаемо.',
  TransientFailure: 'Steam несколько раз не ответил.',
  ErrorRateExceeded: 'Слишком много ошибок в последних ответах Steam.',
};
