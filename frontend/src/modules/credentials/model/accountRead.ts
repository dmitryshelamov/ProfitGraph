const failureLabels: Readonly<Record<string, string>> = {
  venue_unavailable: 'биржа не отвечает',
  venue_rejected: 'биржа отклонила запрос',
  venue_error: 'биржа вернула ошибку',
  unsupported_response: 'непонятный ответ биржи',
  invalid_response: 'непонятный ответ биржи',
  credential_unavailable: 'ключ недоступен',
  venue_unsupported: 'биржа не поддерживается',
  account_not_found: 'ключ больше не видит этот счёт',
};

/** Why reading a key's account (its value or its description) last failed, by the server's reason code. */
export function accountReadFailureLabel(reason: string): string {
  return failureLabels[reason] ?? reason;
}
