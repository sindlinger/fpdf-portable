const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('fpdfApi', {
  pickDir: () => ipcRenderer.invoke('pick-dir'),
  runPipeline: (inputDir, outputStep2, outputStep3) => ipcRenderer.invoke('run-pipeline', { inputDir, outputStep2, outputStep3 }),
  runPipelineFull: (args) => ipcRenderer.invoke('run-pipeline', args),
  readJson: (filePath) => ipcRenderer.invoke('read-json', filePath),
});
