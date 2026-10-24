import { useMutation } from '@tanstack/react-query';
import { type FormEvent, useState } from 'react';

import { useCurrentUser } from '@/modules/accounts';
import { JobProgress } from '@/modules/jobs';
import {
  importCandlesCsv,
  SeriesFields,
  type SeriesSelection,
  startHistoryDownload,
  utcDayStart,
} from '@/modules/market-data';
import { ApiErrorMessage } from '@/shared/ui/ApiErrorMessage';
import { Button } from '@/shared/ui/Button';
import { Card } from '@/shared/ui/Card';
import { DateTime } from '@/shared/ui/DateTime';
import { Field } from '@/shared/ui/Field';
import { Input } from '@/shared/ui/Input';
import { PageHeader } from '@/shared/ui/PageHeader';

interface DownloadResult {
  readonly total?: number;
  readonly cached?: number;
  readonly downloaded?: number;
}

function describeDownload(result: unknown) {
  const counts = (result ?? {}) as DownloadResult;

  return `Свечей в периоде: ${counts.total ?? 0}, уже были в кеше: ${counts.cached ?? 0}, загружено: ${counts.downloaded ?? 0}.`;
}

const emptySeries: SeriesSelection = { venue: '', symbol: 'BTCUSDT', timeframe: '1h' };

function HistoryDownload() {
  const [series, setSeries] = useState(emptySeries);
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [periodError, setPeriodError] = useState<string | null>(null);
  const download = useMutation({ mutationFn: startHistoryDownload });

  function submit(event: FormEvent) {
    event.preventDefault();
    const start = from === '' ? null : utcDayStart(from);
    const end = to === '' ? null : utcDayStart(to);

    if ((from !== '' && !start) || (to !== '' && !end)) {
      setPeriodError('Укажите даты в формате ГГГГ-ММ-ДД.');

      return;
    }

    setPeriodError(null);
    download.mutate({ ...series, from: start, to: end });
  }

  return (
    <Card title="Загрузка истории">
      <form className="space-y-4" onSubmit={submit} aria-label="Загрузка истории">
        <SeriesFields value={series} onChange={setSeries} />
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label="С (UTC), пусто — с начала истории">
            <Input type="date" value={from} onChange={(event) => setFrom(event.target.value)} />
          </Field>
          <Field label="По (UTC), пусто — до последней закрытой свечи">
            <Input type="date" value={to} onChange={(event) => setTo(event.target.value)} />
          </Field>
        </div>
        <p className="text-sm text-muted">
          Загружаются только недостающие свечи; сохранённые не меняются.
        </p>
        {periodError && (
          <p role="alert" className="text-sm text-danger-text">
            {periodError}
          </p>
        )}
        {download.error && (
          <ApiErrorMessage
            error={download.error}
            fallback="Не удалось поставить загрузку в очередь."
          />
        )}
        <Button
          type="submit"
          variant="primary"
          disabled={download.isPending || series.venue === ''}
        >
          Загрузить историю
        </Button>
      </form>
      {download.data && (
        <div className="mt-6 border-t border-line pt-4">
          <JobProgress
            jobId={download.data.id}
            label="Загрузка истории"
            describeResult={describeDownload}
          />
        </div>
      )}
    </Card>
  );
}

function CsvImport() {
  const [series, setSeries] = useState(emptySeries);
  const [file, setFile] = useState<File | null>(null);
  const upload = useMutation({ mutationFn: importCandlesCsv });

  return (
    <Card title="Импорт CSV">
      <form
        className="space-y-4"
        aria-label="Импорт CSV"
        onSubmit={(event) => {
          event.preventDefault();

          if (file) {
            upload.mutate({ ...series, file });
          }
        }}
      >
        <SeriesFields value={series} onChange={setSeries} />
        <Field label="Файл CSV">
          <Input
            type="file"
            accept=".csv,text/csv"
            onChange={(event) => setFile(event.target.files?.[0] ?? null)}
          />
        </Field>
        <p className="text-sm text-muted">
          Заголовок: openTime,open,high,low,close,volume; время в UTC, числа с точкой. Свечи с
          другими значениями заменяются.
        </p>
        {upload.error && (
          <ApiErrorMessage error={upload.error} fallback="Не удалось импортировать файл." />
        )}
        {upload.data && (
          <p role="status" className="text-sm">
            Принято свечей: {upload.data.accepted} (с <DateTime value={upload.data.from} /> по{' '}
            <DateTime value={upload.data.to} />
            ).
          </p>
        )}
        <Button type="submit" disabled={!file || upload.isPending || series.venue === ''}>
          Импортировать
        </Button>
      </form>
    </Card>
  );
}

/** Public history of a pair goes into the shared candle cache; only the Owner may import CSV (stage 3 plan, S3-10). */
export function HistoricalDataPage() {
  const user = useCurrentUser();

  return (
    <div className="mx-auto max-w-4xl space-y-6">
      <PageHeader
        title="Исторические данные"
        meta="Свечи хранятся в общем кеше и используются прогонами всех пользователей."
      />
      <HistoryDownload />
      {user?.role === 'Owner' && <CsvImport />}
    </div>
  );
}
