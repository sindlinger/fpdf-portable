const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('fpdfApi', {
  pickDir: () => ipcRenderer.invoke('pick-dir'),
  runPipeline: (inputDir, outputStep2, outputStep3) => ipcRenderer.invoke('run-pipeline', { inputDir, outputStep2, outputStep3 }),
});
