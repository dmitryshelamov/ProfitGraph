import { useQuery } from '@tanstack/react-query';
import { useDeferredValue, useEffect, useId } from 'react';

import { credentialsQuery } from '@/modules/credentials';
import { Field } from '@/shared/ui/Field';
import { Input } from '@/shared/ui/Input';

import { instrumentsQuery, venuesQuery } from '../api/marketDataApi';
import { type SeriesSelection, timeframeLabel, venuesReadWithKey } from '../model/seriesSelection';

const selectClassName =
  'w-full rounded-md border border-line-strong bg-panel px-3 py-2 text-text outline-none focus:border-link';

/**
 * Venue, pair and timeframe of a history. The pair field suggests the venue's tradable pairs as the user types; any
 * text is accepted and the venue normalizes it, so a pair missing from the list can still be entered. With
 * `withKey`, a venue whose market data is read with the user's own key (T-Invest) also asks for that key and picks the
 * user's first key of the venue.
 */
export function SeriesFields({
  value,
  onChange,
  withKey = false,
}: {
  value: SeriesSelection;
  onChange: (value: SeriesSelection) => void;
  withKey?: boolean;
}) {
  const venues = useQuery(venuesQuery());
  const readWithKey = withKey && venuesReadWithKey.has(value.venue);
  const credentials = useQuery({ ...credentialsQuery(), enabled: readWithKey });
  const keys = readWithKey
    ? (credentials.data ?? []).filter((credential) => credential.venue === value.venue)
    : [];
  const query = useDeferredValue(value.symbol.trim());
  const instruments = useQuery(
    instrumentsQuery(value.venue, query, readWithKey ? value.credentialId : undefined),
  );
  const suggestions = useId();
  const venue = venues.data?.find((candidate) => candidate.id === value.venue);
  const firstKey = keys[0]?.id;

  useEffect(() => {
    const first = venues.data?.[0];

    if (first && !venue) {
      onChange({
        ...value,
        venue: first.id,
        timeframe: first.timeframes.includes(value.timeframe)
          ? value.timeframe
          : (first.timeframes[0] ?? ''),
      });
    }
  }, [venues.data, venue, value, onChange]);

  useEffect(() => {
    const chosen = keys.some((credential) => credential.id === value.credentialId);

    if (!readWithKey && value.credentialId !== undefined) {
      onChange({ ...value, credentialId: undefined });
    } else if (readWithKey && !chosen && value.credentialId !== firstKey && credentials.isSuccess) {
      onChange({ ...value, credentialId: firstKey });
    }
  }, [readWithKey, keys, firstKey, value, onChange, credentials.isSuccess]);

  if (venues.isError) {
    return (
      <p role="alert" className="text-danger-text">
        Не удалось загрузить список площадок.
      </p>
    );
  }

  return (
    <div className="grid gap-4 sm:grid-cols-3">
      <Field label="Площадка">
        <select
          className={selectClassName}
          value={value.venue}
          onChange={(event) => {
            const next = venues.data?.find((candidate) => candidate.id === event.target.value);
            onChange({
              ...value,
              venue: event.target.value,
              timeframe:
                next && !next.timeframes.includes(value.timeframe)
                  ? (next.timeframes[0] ?? '')
                  : value.timeframe,
            });
          }}
        >
          {venues.data?.map((candidate) => (
            <option key={candidate.id} value={candidate.id}>
              {candidate.name}
            </option>
          ))}
        </select>
      </Field>
      {readWithKey && (
        <Field label="Ключ для данных">
          {keys.length > 0 ? (
            <select
              className={selectClassName}
              value={value.credentialId ?? ''}
              onChange={(event) => onChange({ ...value, credentialId: event.target.value })}
            >
              {keys.map((credential) => (
                <option key={credential.id} value={credential.id}>
                  {credential.label} (…{credential.apiKeyLast4})
                </option>
              ))}
            </select>
          ) : (
            <p className="text-sm text-muted">
              Данные этой площадки читаются вашим ключом — добавьте его на странице «Ключи».
            </p>
          )}
        </Field>
      )}
      <Field label="Пара">
        <Input
          list={suggestions}
          value={value.symbol}
          autoComplete="off"
          spellCheck={false}
          placeholder={readWithKey ? 'SBER' : 'BTCUSDT'}
          onChange={(event) => onChange({ ...value, symbol: event.target.value })}
        />
      </Field>
      <datalist id={suggestions}>
        {instruments.data?.map((instrument) => (
          <option key={instrument.symbol} value={instrument.symbol}>
            {instrument.baseAsset}/{instrument.quoteAsset}
          </option>
        ))}
      </datalist>
      <Field label="Таймфрейм">
        <select
          className={selectClassName}
          value={value.timeframe}
          onChange={(event) => onChange({ ...value, timeframe: event.target.value })}
        >
          {(venue?.timeframes ?? []).map((timeframe) => (
            <option key={timeframe} value={timeframe}>
              {timeframeLabel(timeframe)}
            </option>
          ))}
        </select>
      </Field>
    </div>
  );
}
