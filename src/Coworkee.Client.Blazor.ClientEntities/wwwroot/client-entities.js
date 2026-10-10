const open = () => new Promise((resolve, reject) => {
    const request = indexedDB.open('coworkee-client-entities', 1);
    request.onupgradeneeded = () => request.result.createObjectStore('sets');
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
});

async function run(mode, action) {
    const db = await open();
    try {
        return await new Promise((resolve, reject) => {
            const tx = db.transaction('sets', mode);
            const request = action(tx.objectStore('sets'));
            tx.oncomplete = () => resolve(request.result);
            tx.onerror = () => reject(tx.error);
        });
    } finally {
        db.close();
    }
}

export const load = key => run('readonly', store => store.get(key)).then(data => data ?? null);

export const save = (key, data) => run('readwrite', store => store.put(data, key));

export const clear = () => run('readwrite', store => store.clear());
