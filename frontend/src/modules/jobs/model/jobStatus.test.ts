import { isJobFinished, jobPercent, jobStatusLabel } from './jobStatus';

describe('job status', () => {
  it('names statuses in Russian and keeps unknown ones', () => {
    expect(jobStatusLabel('Running')).toBe('Выполняется');
    expect(jobStatusLabel('Paused')).toBe('Приостановлена');
    expect(jobStatusLabel('Archived')).toBe('Archived');
  });

  it('treats completed, failed and cancelled jobs as finished, and a paused one as not', () => {
    expect(
      ['Queued', 'Running', 'Paused', 'Completed', 'Failed', 'Cancelled'].map((status) =>
        isJobFinished({ status }),
      ),
    ).toEqual([false, false, false, true, true, true]);
  });

  it('reports whole percent only once a total is known', () => {
    expect(jobPercent({ completed: 0, total: null })).toBeNull();
    expect(jobPercent({ completed: 0, total: 0 })).toBeNull();
    expect(jobPercent({ completed: 1, total: 3 })).toBe(33);
    expect(jobPercent({ completed: 5, total: 4 })).toBe(100);
  });
});
