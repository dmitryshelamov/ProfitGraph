import { ApiError } from '@/shared/api/client';

/** The API's own messages for a failed request: one per invalid field, or the problem's detail. */
export function ApiErrorMessage({ error, fallback }: { error: Error; fallback: string }) {
  const messages =
    error instanceof ApiError
      ? error.errors.length > 0
        ? error.errors.map((item) => item.message)
        : [error.message]
      : [fallback];

  return (
    <div role="alert" className="space-y-1 text-sm text-danger-text">
      {messages.map((message, index) => (
        <p key={index}>{message}</p>
      ))}
    </div>
  );
}
