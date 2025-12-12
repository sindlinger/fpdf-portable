const inputDir = document.getElementById('inputDir');
const out2 = document.getElementById('out2');
const out3 = document.getElementById('out3');
const out4 = document.getElementById('out4');
const statusEl = document.getElementById('status');
const logsEl = document.getElementById('logs');
const resultsEl = document.getElementById('results');
const runStep4 = document.getElementById('runStep4');

document.getElementById('pickDir').addEventListener('click', async () => {
  const dir = await window.fpdfApi.pickDir();
  if (dir) inputDir.value = dir;
});

document.getElementById('runBtn').addEventListener('click', async () => {
  statusEl.textContent = 'Executando...';
  logsEl.value = '';
  const res = await window.fpdfApi.runPipelineFull({
    inputDir: inputDir.value,
    outputStep2: out2.value,
    outputStep3: out3.value,
    runStep4: runStep4.checked,
    outputStep4: out4.value,
  });
  if (res.ok) {
    statusEl.textContent = 'Concluído';
    logsEl.value = res.logs;
  } else {
    statusEl.textContent = 'Erro: ' + res.error;
    logsEl.value = res.logs;
  }
});

async function loadAndShow(filePath, label) {
  const res = await window.fpdfApi.readJson(filePath);
  if (!res.ok) {
    resultsEl.textContent = `Erro ao ler ${label}: ${res.error}`;
    return;
  }
  const data = res.json;
  if (label === 'documents') {
    const docs = data.documents || [];
    resultsEl.textContent = `${docs.length} documentos\\n` + docs.map(d => `- ${d.doc_label || d.doc_type || 'doc'} (p${d.start_page}-${d.end_page})`).join('\\n');
  } else if (label === 'fields') {
    const best = data.best || [];
    resultsEl.textContent = `${best.length} campos best\\n` + best.map(f => `- ${f.field}: ${f.value}`).join('\\n');
  } else {
    resultsEl.textContent = JSON.stringify(data, null, 2);
  }
}

document.getElementById('loadDocs').addEventListener('click', () => loadAndShow(out3.value, 'documents'));
document.getElementById('loadFields').addEventListener('click', () => loadAndShow(out4.value, 'fields'));
