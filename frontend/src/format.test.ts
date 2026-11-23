import { describe, expect, it } from 'vitest';
import { message, money, statusLabel } from './Banking';

describe('money', () => {
  it('formats amounts in euros with two decimals', () => {
    expect(money('170')).toMatch(/^170,00\s€$/);
    expect(money('0.5')).toMatch(/^0,50\s€$/);
  });
});

describe('message', () => {
  it('returns the message of an Error', () => {
    expect(message(new Error('Premalo sredstev.'))).toBe('Premalo sredstev.');
  });
  it('falls back to a generic text for unknown errors', () => {
    expect(message('nekaj')).toBe('Zahteva ni uspela.');
  });
});

describe('statusLabel', () => {
  it('labels every transfer status', () => {
    expect(statusLabel.PENDING_APPROVAL).toBe('Čaka na odobritev');
    expect(statusLabel.EXECUTED).toBe('Izvedeno');
    expect(statusLabel.REJECTED).toBe('Zavrnjeno');
    expect(statusLabel.FAILED).toBe('Neuspešno');
  });
});
