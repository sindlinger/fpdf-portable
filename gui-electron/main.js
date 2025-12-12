const { app, BrowserWindow, ipcMain, dialog } = require('electron');
const path = require('path');
const { spawn } = require('child_process');

function createWindow() {
  const win = new BrowserWindow({
    width: 1200,
    height: 800,
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
    },
  });
  win.loadFile('index.html');
}

app.whenReady().then(() => {
  createWindow();
  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow();
  });
});

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit();
});

// IPC: run pipeline step (2-fpdf and 3-docid-splitter)
ipcMain.handle('run-pipeline', async (event, args) => {
  const { inputDir, outputStep2, outputStep3 } = args;
  const logs = [];

  function runCmd(cmd, cwd) {
    return new Promise((resolve, reject) => {
      const p = spawn(cmd[0], cmd.slice(1), { cwd, shell: false });
      p.stdout.on('data', (d) => logs.push(d.toString()))
      p.stderr.on('data', (d) => logs.push(d.toString()))
      p.on('close', (code) => {
        code === 0 ? resolve() : reject(new Error(`code ${code}`));
      });
    });
  }

  // Detect fpdf binary
  const repoRoot = path.join(__dirname, '..');
  const binCandidates = [
    path.join(repoRoot, 'fpdf-linux'),
    path.join(repoRoot, 'fpdf-win.exe'),
    path.join(repoRoot, 'bin', 'publish-linux', 'fpdf'),
    path.join(repoRoot, 'bin', 'publish-win', 'fpdf.exe'),
  ];
  const fs = require('fs');
  const fpdfBin = binCandidates.find((b) => fs.existsSync(b));
  if (!fpdfBin) {
    throw new Error('Não encontrei fpdf-linux nem fpdf-win.exe. Faça publish primeiro.');
  }

  const step2 = ['python', 'pipeline/2-fpdf/run.py', '--input-dir', inputDir, '--output', outputStep2];
  const step3 = ['python', 'pipeline/3-docid-splitter/run.py'];

  // step3 lê step2 padrão; se outputStep2 custom, exportar via env
  process.env.FPDF_STEP2 = outputStep2;
  process.env.FPDF_STEP3 = outputStep3;

  try {
    await runCmd(step2, repoRoot);
    await runCmd(step3, repoRoot);
    return { ok: true, logs: logs.join('\n') };
  } catch (err) {
    return { ok: false, error: err.message, logs: logs.join('\n') };
  }
});

ipcMain.handle('pick-dir', async () => {
  const res = await dialog.showOpenDialog({ properties: ['openDirectory'] });
  if (res.canceled || res.filePaths.length === 0) return null;
  return res.filePaths[0];
});
