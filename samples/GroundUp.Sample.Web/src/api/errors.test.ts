import { describe, expect, it } from 'vitest';
import { ApiError, parseErrorBody } from './errors';

describe('parseErrorBody', () => {
  it('reads an OperationResult', () => {
    const error = parseErrorBody(
      400,
      'Bad Request',
      { data: null, success: false, message: 'Name is required.', errors: ['Name is required.'], statusCode: 400, errorCode: 'VALIDATION_FAILED' },
      'cid-1',
    );

    expect(error).toBeInstanceOf(ApiError);
    expect(error.status).toBe(400);
    expect(error.message).toBe('Name is required.');
    expect(error.errorCode).toBe('VALIDATION_FAILED');
    expect(error.errors).toEqual(['Name is required.']);
    expect(error.correlationId).toBe('cid-1');
  });

  it('reads an OperationResult with no errors list', () => {
    const error = parseErrorBody(404, '', { success: false, message: 'Item not found', statusCode: 404, errorCode: null }, null);

    expect(error.message).toBe('Item not found');
    expect(error.errorCode).toBeNull();
    expect(error.errors).toEqual([]);
  });

  it('reads the exception middleware body, taking the correlation ID from it when the header is missing', () => {
    const error = parseErrorBody(500, 'Internal Server Error', { message: 'An unexpected error occurred.', errorCode: 'INTERNAL_ERROR', correlationId: 'cid-body' }, null);

    expect(error.message).toBe('An unexpected error occurred.');
    expect(error.errorCode).toBe('INTERNAL_ERROR');
    expect(error.correlationId).toBe('cid-body');
  });

  it('prefers the header correlation ID over the body', () => {
    const error = parseErrorBody(500, '', { message: 'x', correlationId: 'cid-body' }, 'cid-header');

    expect(error.correlationId).toBe('cid-header');
  });

  it("reads the auth controllers' { error, errorCode }", () => {
    const error = parseErrorBody(401, 'Unauthorized', { error: 'Sign in again.', errorCode: 'REAUTH_REQUIRED' }, 'cid-2');

    expect(error.message).toBe('Sign in again.');
    expect(error.errorCode).toBe('REAUTH_REQUIRED');
  });

  it('reads ProblemDetails, flattening field errors', () => {
    const error = parseErrorBody(
      400,
      'Bad Request',
      { type: 'https://tools.ietf.org/html/rfc9110#section-15.5.1', title: 'One or more validation errors occurred.', status: 400, errors: { Email: ['The Email field is required.'], RoleId: ['Invalid.', 'Missing.'] } },
      null,
    );

    expect(error.message).toBe('One or more validation errors occurred.');
    expect(error.errorCode).toBeNull();
    expect(error.errors).toEqual(['Email: The Email field is required.', 'RoleId: Invalid.', 'RoleId: Missing.']);
  });

  it('uses ProblemDetails detail when present', () => {
    const error = parseErrorBody(403, 'Forbidden', { title: 'Forbidden', status: 403, detail: 'Not allowed here.' }, null);

    expect(error.message).toBe('Not allowed here.');
  });

  it('gives a plain message for an empty body', () => {
    const error = parseErrorBody(401, 'Unauthorized', null, 'cid-3');

    expect(error.message).toBe('HTTP 401 Unauthorized');
    expect(error.errorCode).toBeNull();
    expect(error.correlationId).toBe('cid-3');
  });

  it('gives a plain message for a body that was not JSON', () => {
    const error = parseErrorBody(502, 'Bad Gateway', '<html>proxy error</html>', null);

    expect(error.message).toBe('HTTP 502 Bad Gateway');
    expect(error.body).toBe('<html>proxy error</html>');
  });

  it('gives a plain message for an unknown JSON shape', () => {
    const error = parseErrorBody(418, '', { teapot: true }, null);

    expect(error.message).toBe('HTTP 418');
  });
});
