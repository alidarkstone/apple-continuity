const { contextBridge, ipcRenderer } = require('electron');

const on = (channel) => (cb) => {
  const fn = (_e, payload) => cb(payload);
  ipcRenderer.on(channel, fn);
  return () => ipcRenderer.removeListener(channel, fn);
};

contextBridge.exposeInMainWorld('continuity', {
  settings: {
    get: () => ipcRenderer.invoke('settings:get'),
    set: (patch) => ipcRenderer.invoke('settings:set', patch),
  },
  messages: {
    status: () => ipcRenderer.invoke('messages:status'),
    chats: () => ipcRenderer.invoke('messages:chats'),
    list: (chatId) => ipcRenderer.invoke('messages:list', chatId),
    send: (chatId, text) => ipcRenderer.invoke('messages:send', chatId, text),
    attachmentUrl: (id) => ipcRenderer.invoke('messages:attachment', id),
    onNew: on('messages:new'),
    onTyping: on('messages:typing'),
    onStatus: on('messages:status'),
  },
  facetime: {
    open: (url) => ipcRenderer.invoke('facetime:open', url),
    onHandoff: on('facetime:handoff'),
  },
  bridge: {
    info: () => ipcRenderer.invoke('bridge:info'),
    onCamera: on('camera:status'),
  },
});
