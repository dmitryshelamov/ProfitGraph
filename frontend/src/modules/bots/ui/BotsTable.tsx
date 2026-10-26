import { Link } from '@tanstack/react-router';

import { botTitle } from '../model/botDisplay';
import type { Bot } from '../model/types';
import { BotStatusBadge } from './BotStatusBadge';

export function BotsTable({ bots }: { bots: readonly Bot[] }) {
  return (
    <table className="w-full text-left text-sm">
      <thead className="text-text-secondary">
        <tr>
          <th className="py-2">Бот</th>
          <th className="py-2">Биржа</th>
          <th className="py-2">Капитал</th>
          <th className="py-2">Состояние</th>
        </tr>
      </thead>
      <tbody>
        {bots.map((bot) => (
          <tr key={bot.id} className="border-t border-line">
            <td className="py-2">
              <Link to="/bots/$botId" params={{ botId: bot.id }} className="hover:underline">
                {botTitle(bot)}
              </Link>
            </td>
            <td className="py-2">{bot.venue}</td>
            <td className="py-2 font-mono">{bot.allocationCap}</td>
            <td className="py-2">
              <BotStatusBadge bot={bot} />
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
