import type { City } from './cities';
import { CONFIG } from './config';
import { crewSpeedMultiplier } from './crew';
import type { GameTime } from './gameTime';
import { marketFor } from './market';
import type { Player } from './player';

// Closable, screen-centred panel describing the clicked city: a few facts, then
// the city's market — one row per product it trades, with buy/sell buttons.
// To show more about a city, add an entry to FIELDS.
const FIELDS: Array<{ label: string; value(city: City): string }> = [
  { label: 'coords', value: (c) => `${Math.round(c.location.x)}, ${Math.round(c.location.y)}` },
  { label: 'size', value: (c) => c.size },
];

// Quantities offered per row, for each direction.
const STEPS = [1, 10];

export interface CityPopup {
  element: HTMLDivElement;
  show(city: City): void;
  hide(): void;
  isOpen(): boolean;
  /** Re-read the player's purse and hold (the crew eats between trades). */
  refresh(): void;
}

export function createCityPopup(player: Player, time: GameTime): CityPopup {
  const cfg = CONFIG.cityPopup;

  const element = document.createElement('div');
  element.style.cssText = [
    'position: fixed', 'top: 50%', 'left: 50%', 'transform: translate(-50%, -50%)',
    `width: ${cfg.widthVw}vw`, `min-width: ${cfg.minWidthPx}px`, `max-width: ${cfg.maxWidthPx}px`,
    `max-height: ${cfg.maxHeightVh}vh`, 'overflow-y: auto',
    `font: ${cfg.fontPx}px system-ui, sans-serif`, 'color: #ddd',
    'background: rgba(12, 12, 14, 0.94)', 'border: 1px solid #666', 'border-radius: 8px',
    'box-shadow: 0 12px 40px rgba(0, 0, 0, 0.6)',
    'padding: 20px 24px', 'z-index: 20', 'display: none',
  ].join(';');

  const header = document.createElement('div');
  header.style.cssText = 'display: flex; align-items: baseline; gap: 16px; margin-bottom: 10px';

  const title = document.createElement('div');
  title.style.cssText = `font: 700 ${cfg.titleFontPx}px system-ui, sans-serif; color: #fff; flex: 1`;

  const close = document.createElement('button');
  close.textContent = '✕';
  close.title = 'Close (Esc)';
  close.style.cssText = [
    'all: unset', 'cursor: pointer', 'color: #aaa',
    'font: 18px monospace', 'line-height: 1', 'padding: 4px 6px',
  ].join(';');
  close.addEventListener('click', () => hide());
  close.addEventListener('pointerdown', (e) => e.stopPropagation());

  header.append(title, close);

  const facts = document.createElement('div');
  facts.style.cssText = `white-space: pre; line-height: 1.6; font: ${cfg.fontPx - 2}px monospace; color: #bbb`;

  const headingCss = [
    'margin: 18px 0 8px', 'padding-bottom: 6px', 'border-bottom: 1px solid #444',
    `font: 600 ${cfg.fontPx + 4}px system-ui, sans-serif`, 'color: #fff',
  ].join(';');

  // Crew: hiring hall, large cities only (hidden everywhere else).
  const crewHeading = document.createElement('div');
  crewHeading.textContent = 'Crew';
  crewHeading.style.cssText = headingCss;

  const crewSection = document.createElement('div');
  crewSection.style.cssText = 'display: flex; align-items: center; gap: 14px; padding: 4px 0';

  const crewCount = document.createElement('div');
  crewCount.style.cssText = `flex: 1; font: ${cfg.fontPx}px monospace; color: #ddd`;

  const crewActions = document.createElement('div');
  crewActions.style.cssText = 'display: flex; gap: 6px';
  // Free, and no haggling involved, so these don't cost the player any time.
  const addCrew = button('Add', () => player.changeCrew(1), { costsTime: false });
  const removeCrew = button('Remove', () => player.changeCrew(-1), { costsTime: false });
  crewActions.append(addCrew, removeCrew);
  crewSection.append(crewCount, crewActions);

  const marketHeading = document.createElement('div');
  marketHeading.textContent = 'Market';
  marketHeading.style.cssText = headingCss;

  const market = document.createElement('div');

  const purse = document.createElement('div');
  purse.style.cssText = [
    'margin-top: 16px', 'padding-top: 10px', 'border-top: 1px solid #444',
    'display: flex', 'gap: 24px', `font: 600 ${cfg.fontPx}px monospace`,
  ].join(';');

  element.append(header, facts, crewHeading, crewSection, marketHeading, market, purse);

  let current: City | null = null;
  // Per-row refreshers, rebuilt whenever a different city is shown.
  let refreshers: Array<() => void> = [];

  function button(
    label: string,
    onClick: () => void,
    { costsTime = true }: { costsTime?: boolean } = {},
  ): HTMLButtonElement {
    const b = document.createElement('button');
    b.textContent = label;
    b.style.cssText = [
      'all: unset', 'cursor: pointer', 'box-sizing: border-box',
      'padding: 4px 10px', 'min-width: 58px', 'text-align: center',
      'border: 1px solid #666', 'border-radius: 4px', 'background: #23262c',
      `font: ${cfg.fontPx - 2}px system-ui, sans-serif`, 'color: #eee',
    ].join(';');
    b.addEventListener('pointerdown', (e) => e.stopPropagation());
    b.addEventListener('click', () => {
      if (b.getAttribute('aria-disabled') === 'true') return;
      onClick();
      // Haggling takes time: every market button that actually does something
      // costs the player ten minutes of the day.
      if (costsTime) time.advance(CONFIG.gameTime.minutesPerTransaction);
      refresh();
    });
    return b;
  }

  function setEnabled(b: HTMLButtonElement, enabled: boolean) {
    b.setAttribute('aria-disabled', enabled ? 'false' : 'true');
    b.style.opacity = enabled ? '1' : '0.35';
    b.style.cursor = enabled ? 'pointer' : 'default';
  }

  function refresh() {
    for (const r of refreshers) r();
    const { max, cargoPerCrew } = CONFIG.crew;
    crewCount.textContent =
      `${player.crew} aboard (max ${max})   ` +
      `speed x${crewSpeedMultiplier(player.crew, CONFIG.crew).toFixed(2)}   ` +
      `cargo ${player.totalUnits()}/${player.cargoCapacity()} (${cargoPerCrew}/hand)`;
    // Dismissing a hand is refused when their share of the hold is in use.
    setEnabled(addCrew, player.canChangeCrew(1));
    setEnabled(removeCrew, player.canChangeCrew(-1));
    removeCrew.title = player.crew > CONFIG.crew.min && !player.canChangeCrew(-1)
      ? 'Not enough hold space without them — sell some cargo first'
      : '';
    purse.textContent =
      `money ${player.money}    cargo ${player.totalUnits()}/${player.cargoCapacity()} units`;
    purse.style.color = player.money < 0 ? '#ff8b7a' : '#ffd166';
  }

  function buildMarket(city: City) {
    market.textContent = '';
    refreshers = [];

    for (const quote of marketFor(city)) {
      const { product, buyPrice, sellPrice, priceMultiplier } = quote;
      // A local price quirk colours the whole row: red = this city charges
      // over the odds, green = a bargain.
      const dear = priceMultiplier > 1;
      const quirky = priceMultiplier !== 1;
      const tint = !quirky
        ? CONFIG.market.normalColor
        : dear ? CONFIG.market.dearColor : CONFIG.market.cheapColor;

      const row = document.createElement('div');
      row.style.cssText = [
        'display: flex', 'align-items: center', 'gap: 14px',
        'padding: 8px 0', 'border-bottom: 1px solid #2a2a2e',
      ].join(';');

      const info = document.createElement('div');
      info.style.cssText = 'flex: 1; min-width: 0';
      const name = document.createElement('div');
      name.textContent = quirky
        ? `${product.name}  ${dear ? '▲' : '▼'} ${Math.round(priceMultiplier * 100)}%`
        : product.name;
      name.title = quirky
        ? `${city.name} trades ${product.name} at ${priceMultiplier}x the usual price`
        : '';
      name.style.cssText = `font: 600 ${cfg.fontPx}px system-ui, sans-serif; color: ${tint}`;
      const desc = document.createElement('div');
      desc.textContent = product.description;
      desc.style.cssText = `font: ${cfg.fontPx - 3}px system-ui, sans-serif; color: #999`;
      info.append(name, desc);

      const prices = document.createElement('div');
      prices.textContent = `buy ${buyPrice}\nsell ${sellPrice}`;
      prices.style.cssText = `white-space: pre; text-align: right; font: ${cfg.fontPx - 2}px monospace; color: ${quirky ? tint : '#cfd6e4'}`;

      const held = document.createElement('div');
      held.style.cssText = `min-width: 72px; text-align: right; font: ${cfg.fontPx - 1}px monospace; color: #9fe8a0`;

      const actions = document.createElement('div');
      actions.style.cssText = 'display: flex; gap: 6px';
      const buyButtons: HTMLButtonElement[] = [];
      const sellButtons: HTMLButtonElement[] = [];
      for (const n of STEPS) {
        const b = button(`Buy ${n}`, () => player.buy(product.id, n, buyPrice));
        buyButtons.push(b);
        actions.appendChild(b);
      }
      for (const n of STEPS) {
        const b = button(`Sell ${n}`, () => player.sell(product.id, n, sellPrice));
        sellButtons.push(b);
        actions.appendChild(b);
      }

      row.append(info, prices, held, actions);
      market.appendChild(row);

      refreshers.push(() => {
        const have = player.held(product.id);
        held.textContent = `have ${have}`;
        held.style.color = have > 0 ? '#9fe8a0' : '#666';
        // Selling is capped by what's aboard, buying by the free space — the
        // purse never blocks a trade, since money may go negative. A partial
        // buy is fine (Buy 10 with 3 spaces buys 3), so the button only greys
        // out when the hold is completely full.
        const space = player.cargoSpace();
        STEPS.forEach((n, i) => {
          setEnabled(sellButtons[i]!, have >= n);
          setEnabled(buyButtons[i]!, space > 0);
        });
      });
    }

    if (!refreshers.length) {
      const empty = document.createElement('div');
      empty.textContent = 'This city trades nothing.';
      empty.style.cssText = 'color: #888; padding: 8px 0';
      market.appendChild(empty);
    }
  }

  function hide() {
    element.style.display = 'none';
    current = null;
  }

  return {
    element,
    show(city) {
      title.textContent = city.name;
      facts.textContent = FIELDS.map((f) => `${f.label.padEnd(8)}${f.value(city)}`).join('\n');
      // Only the large cities have a hiring hall.
      const hiring = city.size === 'large';
      crewHeading.style.display = hiring ? 'block' : 'none';
      crewSection.style.display = hiring ? 'flex' : 'none';
      if (city !== current) {
        current = city;
        buildMarket(city);
      }
      refresh();
      element.style.display = 'block';
    },
    hide,
    isOpen: () => element.style.display !== 'none',
    refresh,
  };
}
