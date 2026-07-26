'use strict';

// Run with:
// node --test .\tests\IGoLibrary.Ex.Tests\MobileControlBluetoothScannerTests.cjs

const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');

const scanner = require(path.resolve(
  __dirname,
  '../../src/IGoLibrary.Ex.Desktop/Resources/MobileControl/MobileControlBluetoothScanner.js'));

function createPayload({
  prefix = [0x02, 0x15],
  uuidBytes = Array.from({ length: 16 }, (_, index) => index),
  major = 42,
  minor = 258,
  txPower = -59,
  length = 23
} = {}) {
  const bytes = new Uint8Array(length);
  if (length > 0) bytes[0] = prefix[0];
  if (length > 1) bytes[1] = prefix[1];
  for (let index = 0; index < uuidBytes.length && index + 2 < length; index++) {
    bytes[index + 2] = uuidBytes[index];
  }
  if (length >= 20) {
    bytes[18] = (major >>> 8) & 0xFF;
    bytes[19] = major & 0xFF;
  }
  if (length >= 22) {
    bytes[20] = (minor >>> 8) & 0xFF;
    bytes[21] = minor & 0xFF;
  }
  if (length >= 23) bytes[22] = txPower & 0xFF;
  return new DataView(bytes.buffer);
}

function advertisement(companyIdentifier, payload, rssi = -48) {
  return {
    manufacturerData: new Map([[companyIdentifier, payload]]),
    rssi
  };
}

test('parses a standard Apple iBeacon with exact offsets and provenance', () => {
  const parsed = scanner.parseIBeaconAdvertisement(
    advertisement(scanner.IBEACON_COMPANY_IDENTIFIER, createPayload()),
    false,
    1_234);

  assert.deepEqual(parsed, {
    uuid: '00010203-0405-0607-0809-0A0B0C0D0E0F',
    major: 42,
    minor: 258,
    rssi: -48,
    txPower: -59,
    lastSeen: 1_234,
    seenCount: 1,
    companyIdentifier: 0x004C,
    isStandardIBeacon: true
  });
});

test('strictly ignores wrong lengths, prefixes and missing manufacturer data', () => {
  assert.equal(scanner.parseIBeaconAdvertisement(
    advertisement(0x004C, createPayload({ length: 22 }))), null);
  assert.equal(scanner.parseIBeaconAdvertisement(
    advertisement(0x004C, createPayload({ prefix: [0x02, 0x16] }))), null);
  assert.equal(scanner.parseIBeaconAdvertisement({ manufacturerData: null }), null);
});

test('compatibility mode accepts non-Apple payloads and marks them as suspected', () => {
  const event = advertisement(0x1234, createPayload());

  assert.equal(scanner.parseIBeaconAdvertisement(event, false), null);
  const parsed = scanner.parseIBeaconAdvertisement(event, true, 2_345);

  assert.equal(parsed.companyIdentifier, 0x1234);
  assert.equal(parsed.isStandardIBeacon, false);
  assert.equal(parsed.major, 42);
  assert.equal(parsed.minor, 258);
});

test('aggregation keeps standard and compatibility sources separate', () => {
  const records = new Map();
  const standard = scanner.parseIBeaconAdvertisement(
    advertisement(0x004C, createPayload()), true, 1_000);
  const compatible = scanner.parseIBeaconAdvertisement(
    advertisement(0x1234, createPayload()), true, 2_000);

  scanner.upsertIBeaconRecord(records, standard);
  scanner.upsertIBeaconRecord(records, compatible);
  assert.equal(records.size, 2);

  const repeatedStandard = {
    ...standard,
    rssi: -35,
    lastSeen: 3_000
  };
  scanner.upsertIBeaconRecord(records, repeatedStandard);
  const updated = records.get(scanner.createIBeaconRecordKey(standard));
  assert.equal(updated.seenCount, 2);
  assert.equal(updated.rssi, -35);
  assert.equal(updated.lastSeen, 3_000);
});

test('record limit rejects new sources but still updates existing sources', () => {
  const records = new Map();
  const record = companyIdentifier => ({
    uuid: '00010203-0405-0607-0809-0A0B0C0D0E0F',
    major: 42,
    minor: 258,
    rssi: -50,
    txPower: -59,
    lastSeen: companyIdentifier,
    seenCount: 1,
    companyIdentifier,
    isStandardIBeacon: companyIdentifier === 0x004C
  });

  assert.equal(scanner.upsertIBeaconRecord(records, record(1), 2).accepted, true);
  assert.equal(scanner.upsertIBeaconRecord(records, record(2), 2).accepted, true);
  const rejected = scanner.upsertIBeaconRecord(records, record(3), 2);
  assert.equal(rejected.accepted, false);
  assert.equal(rejected.limitReached, true);
  assert.equal(records.size, 2);

  const updated = scanner.upsertIBeaconRecord(records, {
    ...record(1),
    rssi: -30,
    lastSeen: 4_000
  }, 2);
  assert.equal(updated.accepted, true);
  assert.equal(records.get(scanner.createIBeaconRecordKey(record(1))).seenCount, 2);
});

test('sorts by strongest RSSI and then most recent observation', () => {
  const records = new Map([
    ['weak', { rssi: -80, lastSeen: 3 }],
    ['older', { rssi: -40, lastSeen: 1 }],
    ['newer', { rssi: -40, lastSeen: 2 }],
    ['unknown', { rssi: null, lastSeen: 4 }]
  ]);

  assert.deepEqual(
    scanner.sortIBeaconRecords(records),
    [records.get('newer'), records.get('older'), records.get('weak'), records.get('unknown')]);
});

test('maps Android NotFoundError to actionable nearby-device permission guidance', () => {
  const message = scanner.mapBluetoothScanError({ name: 'NotFoundError' });
  assert.match(message, /附近的设备/);
  assert.match(message, /Chrome 网站设置/);
  assert.match(
    scanner.mapBluetoothScanError({ name: 'UnknownError' }),
    /扫描失败（UnknownError）/);
});
