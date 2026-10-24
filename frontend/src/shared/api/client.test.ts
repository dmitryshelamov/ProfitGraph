import { ApiError, unwrap } from './client';

function response(status: number, statusText = ''): Response {
  return new Response(null, { status, statusText });
}

function captureError(action: () => unknown): ApiError {
  try {
    action();
  } catch (error) {
    if (error instanceof ApiError) {
      return error;
    }

    throw error;
  }

  throw new Error('Expected ApiError to be thrown.');
}

describe('unwrap', () => {
  it('returns the data of a successful response', () => {
    const data = { id: 'strategy-1' };

    expect(unwrap({ data, response: response(200) })).toBe(data);
  });

  it('builds ApiError from Problem Details', () => {
    const errors = [{ code: 'required', field: 'name', message: 'Укажите название.' }];
    const problem = {
      title: 'Validation failed',
      detail: 'Запрос содержит ошибки.',
      code: 'validation_failed',
      errors,
    };

    const error = captureError(() =>
      unwrap({ error: problem, response: response(400, 'Bad Request') }),
    );

    expect(error.name).toBe('ApiError');
    expect(error.status).toBe(400);
    expect(error.code).toBe('validation_failed');
    expect(error.message).toBe('Запрос содержит ошибки.');
    expect(error.errors).toEqual(errors);
    expect(error.payload).toBe(problem);
  });

  it('uses the title when Problem Details have no detail', () => {
    const error = captureError(() =>
      unwrap({ error: { title: 'Not Found', code: 'not_found' }, response: response(404) }),
    );

    expect(error.message).toBe('Not Found');
  });

  it('falls back to http_<status> and statusText without Problem Details', () => {
    const error = captureError(() => unwrap({ response: response(503, 'Service Unavailable') }));

    expect(error.status).toBe(503);
    expect(error.code).toBe('http_503');
    expect(error.message).toBe('Service Unavailable');
    expect(error.errors).toEqual([]);
  });

  it('throws when a successful response has no data', () => {
    const error = captureError(() => unwrap({ response: response(200, 'OK') }));

    expect(error.code).toBe('http_200');
  });
});
