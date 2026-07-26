(function (root, factory) {
  const api = factory();
  if (typeof module !== 'undefined' && module.exports) {
    module.exports = api;
  } else {
    root.MobileControlBluetoothScanner = api;
  }
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  'use strict';

  const IBEACON_COMPANY_IDENTIFIER = 0x004C;
  const IBEACON_DATA_PREFIX = Object.freeze([0x02, 0x15]);
  const DEFAULT_MAX_RECORDS = 100;

  function mapBluetoothScanError(error) {
    const errorName = error && typeof error.name === 'string' ? error.name : '未知异常';
    const messages = {
      NotAllowedError: '用户拒绝了蓝牙权限，或浏览器策略阻止了扫描',
      NotFoundError: '未获得蓝牙扫描所需的系统权限。请在 Android 设置中允许 Chrome 使用“附近的设备”，并在 Chrome 网站设置中允许蓝牙后重试',
      SecurityError: '页面不是可信 HTTPS，或 Permissions Policy 禁止使用蓝牙',
      InvalidStateError: '系统蓝牙未开启，或蓝牙适配器当前不可用',
      NotSupportedError: '当前浏览器未实现 BLE 广播扫描',
      AbortError: '扫描请求已被取消',
      TypeError: '实验性 Web Bluetooth API 与当前扫描参数不兼容'
    };
    return messages[errorName] || `扫描失败（${errorName}），请重新检测兼容性后重试`;
  }

  function isIBeaconPayload(data) {
    return data instanceof DataView
      && data.byteLength === 23
      && data.getUint8(0) === IBEACON_DATA_PREFIX[0]
      && data.getUint8(1) === IBEACON_DATA_PREFIX[1];
  }

  function findIBeaconPayload(event, allowNonStandardCompany) {
    if (!event?.manufacturerData || typeof event.manufacturerData.get !== 'function') return null;

    const standardData = event.manufacturerData.get(IBEACON_COMPANY_IDENTIFIER);
    if (isIBeaconPayload(standardData)) {
      return {
        data: standardData,
        companyIdentifier: IBEACON_COMPANY_IDENTIFIER,
        isStandardIBeacon: true
      };
    }
    if (!allowNonStandardCompany || typeof event.manufacturerData.entries !== 'function') return null;

    for (const [companyIdentifier, candidate] of event.manufacturerData.entries()) {
      if (companyIdentifier !== IBEACON_COMPANY_IDENTIFIER && isIBeaconPayload(candidate)) {
        return {
          data: candidate,
          companyIdentifier,
          isStandardIBeacon: false
        };
      }
    }
    return null;
  }

  function parseIBeaconAdvertisement(event, allowNonStandardCompany = false, now = Date.now()) {
    const match = findIBeaconPayload(event, allowNonStandardCompany);
    if (!match) return null;

    const uuidHex = [];
    for (let offset = 2; offset <= 17; offset++) {
      uuidHex.push(match.data.getUint8(offset).toString(16).padStart(2, '0').toUpperCase());
    }
    const compactUuid = uuidHex.join('');
    const uuid = [
      compactUuid.slice(0, 8),
      compactUuid.slice(8, 12),
      compactUuid.slice(12, 16),
      compactUuid.slice(16, 20),
      compactUuid.slice(20)
    ].join('-');

    return {
      uuid,
      major: match.data.getUint16(18, false),
      minor: match.data.getUint16(20, false),
      rssi: Number.isFinite(event.rssi) ? event.rssi : null,
      txPower: match.data.getInt8(22),
      lastSeen: now,
      seenCount: 1,
      companyIdentifier: match.companyIdentifier,
      isStandardIBeacon: match.isStandardIBeacon
    };
  }

  function createIBeaconRecordKey(record) {
    return `${record.uuid}|${record.major}|${record.minor}|${record.companyIdentifier}`;
  }

  function upsertIBeaconRecord(records, parsed, maxRecords = DEFAULT_MAX_RECORDS) {
    const key = createIBeaconRecordKey(parsed);
    const existing = records.get(key);
    if (!existing && records.size >= maxRecords) {
      return { accepted: false, isNew: false, limitReached: true, key };
    }

    records.set(key, existing
      ? {
          uuid: parsed.uuid,
          major: parsed.major,
          minor: parsed.minor,
          rssi: parsed.rssi,
          txPower: parsed.txPower,
          lastSeen: parsed.lastSeen,
          seenCount: existing.seenCount + 1,
          companyIdentifier: parsed.companyIdentifier,
          isStandardIBeacon: parsed.isStandardIBeacon
        }
      : parsed);
    return {
      accepted: true,
      isNew: !existing,
      limitReached: false,
      key
    };
  }

  function sortIBeaconRecords(records) {
    return Array.from(records.values()).sort((left, right) => {
      const leftRssi = Number.isFinite(left.rssi) ? left.rssi : Number.NEGATIVE_INFINITY;
      const rightRssi = Number.isFinite(right.rssi) ? right.rssi : Number.NEGATIVE_INFINITY;
      return rightRssi - leftRssi || right.lastSeen - left.lastSeen;
    });
  }

  return Object.freeze({
    IBEACON_COMPANY_IDENTIFIER,
    IBEACON_DATA_PREFIX,
    DEFAULT_MAX_RECORDS,
    mapBluetoothScanError,
    isIBeaconPayload,
    findIBeaconPayload,
    parseIBeaconAdvertisement,
    createIBeaconRecordKey,
    upsertIBeaconRecord,
    sortIBeaconRecords
  });
});
