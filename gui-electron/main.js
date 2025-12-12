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

// IPC: run pipeline steps (2-fpdf, 3-docid, opcional 4-extract)
ipcMain.handle('run-pipeline', async (event, args) => {
  const { inputDir, outputStep2, outputStep3, runStep4, outputStep4 } = args;
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

  // Detecta binário do fpdf (no repo fpdf-portable)
  const fpdfRoot = path.join(__dirname, '..');
  const envBin = process.env.FPDF_BIN;
  const binCandidates = [
    envBin,
    path.join(fpdfRoot, 'fpdf-linux'),
    path.join(fpdfRoot, 'fpdf-win.exe'),
    path.join(fpdfRoot, 'bin', 'publish-linux', 'fpdf'),
    path.join(fpdfRoot, 'bin', 'publish-win', 'fpdf.exe'),
    'dotnet', // fallback para usar DLL
  ];
  const fs = require('fs');
  const fpdfBin = binCandidates.find((b) => b && (b === 'dotnet' || fs.existsSync(b)));
  if (!fpdfBin) throw new Error('Não encontrei fpdf-linux nem fpdf-win.exe. Faça publish primeiro.');

  // Base do repositório pipeline (irmão do fpdf-portable)
  const pipelineRoot = path.join(__dirname, '..', '..', 'pipeline');

  const step2 = ['python', '2-fpdf/run.py', '--input-dir', inputDir, '--output', outputStep2];
  const step3 = ['python', '3-docid-splitter/run.py'];
  const step4 = ['python', '4-extract/run.py', '--input', outputStep3, '--output', outputStep4];

  // step3 lê step2 padrão; se outputStep2 custom, exportar via env
  process.env.FPDF_STEP2 = outputStep2;
  process.env.FPDF_STEP3 = outputStep3;
  process.env.FPDF_BIN = fpdfBin;

  try {
    await runCmd(step2, pipelineRoot);
    await runCmd(step3, pipelineRoot);
    if (runStep4) {
      await runCmd(step4, pipelineRoot);
    }
    return { ok: true, logs: logs.join('\n') };
  } catch (err) {
    return { ok: false, error: err.message, logs: logs.join('\n') };
  }
});

// Ler JSON de resultado
ipcMain.handle('read-json', async (event, filePath) => {
  const fs = require('fs');
  try {
    const data = fs.readFileSync(filePath, 'utf-8');
    return { ok: true, json: JSON.parse(data) };
  } catch (err) {
    return { ok: false, error: err.message };
  }
});

ipcMain.handle('pick-dir', async () => {
  const res = await dialog.showOpenDialog({ properties: ['openDirectory'] });
  if (res.canceled || res.filePaths.length === 0) return null;
  return res.filePaths[0];
});
