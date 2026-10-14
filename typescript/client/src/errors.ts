/** The problem details a Coworkee API answers with: title, the error code of the result and validation messages per field. */
export type Problem = {
  title?: string;
  detail?: string;
  status?: number;
  code?: string;
  errors?: Record<string, string[]>;
};

/** A failed API call. */
export class ApiError extends Error {
  readonly status: number;
  readonly problem?: Problem;

  constructor(status: number, problem?: Problem) {
    super(problemMessage(status, problem));
    this.name = 'ApiError';
    this.status = status;
    this.problem = problem;
  }

  get code() { return this.problem?.code; }
}

/** One line for the user: the validation messages, else the detail or title, else the status. */
export function problemMessage(status: number, problem?: Problem): string {
  const validation = Object.values(problem?.errors ?? {}).flat().filter(Boolean);
  if (validation.length > 0) return validation.join(' ');
  return problem?.detail || problem?.title || `The API answered ${status}.`;
}

/** Reads the problem of a failed response; bodies that are no JSON give none. */
export async function problemOf(response: Response): Promise<Problem | undefined> {
  try {
    const text = await response.text();
    return text ? JSON.parse(text) as Problem : undefined;
  } catch {
    return undefined;
  }
}

/** The data of an openapi-fetch result, or an ApiError. */
export function unwrap<T>(result: { data?: T; error?: unknown; response: Response }): T {
  if (result.response.ok) return result.data as T;
  throw new ApiError(result.response.status, typeof result.error === 'object' && result.error ? result.error as Problem : undefined);
}
