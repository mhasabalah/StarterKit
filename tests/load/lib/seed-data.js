import { SharedArray } from 'k6/data';
import { SEED_FILE } from './config.js';

// Written by seed.js. Loaded once in the init context and shared by every VU.
const seed = new SharedArray('seed', () => [JSON.parse(open(SEED_FILE))]);

export function seedData() {
  return seed[0];
}

export function pick(list) {
  return list[Math.floor(Math.random() * list.length)];
}
