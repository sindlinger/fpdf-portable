const inputDir = document.getElementById('inputDir');
const out2 = document.getElementById('out2');
const out3 = document.getElementById('out3');
const statusEl = document.getElementById('status');
const logsEl = document.getElementById('logs');

document.getElementById('pickDir').addEventListener('click', async () => {
  const dir = await window.fpdfApi.pickDir();
  if (dir) inputDir.value = dir;
});

document.getElementById('runBtn').addEventListener('click', async () => {
  statusEl.textContent = 'Executando...';
  logsEl.value = '';
  const res = await window.fpdfApi.runPipeline(inputDir.value, out2.value, out3.value);
  if (res.ok) {
    statusEl.textContent = 'Concluído';
    logsEl.value = res.logs;
  } else {
    statusEl.textContent = 'Erro: ' + res.error;
    logsEl.value = res.logs;
  }
});
