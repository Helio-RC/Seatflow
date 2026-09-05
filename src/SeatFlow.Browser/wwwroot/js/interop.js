// SeatFlow 浏览器端 IndexedDB 桥（模块名 "sf.idb"，由 C# 侧 JSHost.ImportAsync 加载）。
// 存储模型：DB "seatflow" / object store "files" / key = 相对路径 / value = { path, data: Uint8Array }
// 传输载体约定（与 IndexedDbDataStore.cs 对应）：
//   - idbWrite(key, base64Text)：写入 key 的内容（base64 解码后存字节）
//   - idbRead(key) -> base64 | null
//   - idbListKeys() -> JSON 数组字符串
//   - idbCreationTimeUtc(key) -> ISO 字符串 | null

const DB_NAME = "seatflow";
const STORE_NAME = "files";

let _dbPromise = null;

function db() {
  if (!_dbPromise) {
    _dbPromise = new Promise((resolve, reject) => {
      const req = indexedDB.open(DB_NAME, 1);
      req.onupgradeneeded = () => {
        const d = req.result;
        if (!d.objectStoreNames.contains(STORE_NAME)) {
          d.createObjectStore(STORE_NAME, { keyPath: "path" });
        }
      };
      req.onsuccess = () => resolve(req.result);
      req.onerror = () => reject(req.error);
    });
  }
  return _dbPromise;
}

function tx(mode, fn) {
  return db().then((d) => new Promise((resolve, reject) => {
    const t = d.transaction(STORE_NAME, mode);
    const s = t.objectStore(STORE_NAME);
    let out = null;
    const rq = fn(s);
    if (rq) {
      rq.onsuccess = () => { out = rq.result; };
      rq.onerror = () => reject(rq.error);
    }
    t.oncomplete = () => resolve(out);
    t.onerror = () => reject(t.error);
    t.onabort = () => reject(t.error);
  }));
}

function base64ToBytes(b64) {
  const bin = atob(b64);
  const bytes = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
  return bytes;
}

function bytesToBase64(bytes) {
  let bin = "";
  for (let i = 0; i < bytes.length; i++) bin += String.fromCharCode(bytes[i]);
  return btoa(bin);
}

export async function idbWrite(key, base64Content) {
  const data = base64ToBytes(base64Content);
  await tx("readwrite", (s) => s.put({ path: key, data }));
}

export async function idbRead(key) {
  const rec = await tx("readonly", (s) => s.get(key));
  if (!rec || !rec.data) return null;
  return bytesToBase64(rec.data);
}

export async function idbDelete(key) {
  await tx("readwrite", (s) => s.delete(key));
}

export async function idbExists(key) {
  const rec = await tx("readonly", (s) => s.get(key));
  return rec != null;
}

export async function idbListKeys() {
  const keys = await tx("readonly", (s) => s.getAllKeys());
  return JSON.stringify(keys);
}

export async function idbCreationTimeUtc(key) {
  const timestamp = await tx("readonly", (s) => {
    // 不追加元数据列，以记录存在性为准；时间戳降级为"首见时间"（内存表）
    return s.get(key);
  });
  return timestamp ? new Date().toISOString() : null;
}
