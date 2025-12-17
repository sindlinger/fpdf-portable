using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FilterPDF.Utils
{
    public class LaudoHashDbEntry
    {
        public string Hash { get; set; } = "";
        public string Especie { get; set; } = "";
        public string Perito { get; set; } = "";
        public string Cpf { get; set; } = "";
        public string Especialidade { get; set; } = "";
    }

    /// <summary>
    /// Banco simples de hashes de laudos para lookup em tempo de execução.
    /// Suporta JSON array ou JSONL, com campos: laudo_hash/hash, especie, perito, cpf, especialidade.
    /// </summary>
    public class LaudoHashDb
    {
        private readonly Dictionary<string, LaudoHashDbEntry> _byHash = new(StringComparer.OrdinalIgnoreCase);

        public static LaudoHashDb? Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            var db = new LaudoHashDb();
            try
            {
                var text = File.ReadAllText(path);
                if (text.TrimStart().StartsWith("["))
                {
                    var arr = JsonConvert.DeserializeObject<JArray>(text);
                    db.AddArray(arr);
                }
                else
                {
                    using var reader = new StreamReader(path);
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        try
                        {
                            var obj = JObject.Parse(line);
                            db.AddObject(obj);
                        }
                        catch { /* ignore bad line */ }
                    }
                }
                return db;
            }
            catch
            {
                return null;
            }
        }

        private void AddArray(JArray? arr)
        {
            if (arr == null) return;
            foreach (var item in arr)
            {
                if (item is JObject o) AddObject(o);
            }
        }

        private void AddObject(JObject obj)
        {
            string hash = obj.Value<string>("laudo_hash")
                       ?? obj.Value<string>("hash")
                       ?? obj.Value<string>("LAUDO_HASH")
                       ?? "";
            if (string.IsNullOrWhiteSpace(hash)) return;
            var entry = new LaudoHashDbEntry
            {
                Hash = hash,
                Especie = obj.Value<string>("especie") ?? obj.Value<string>("ESPECIE") ?? "",
                Perito = obj.Value<string>("perito") ?? obj.Value<string>("PERITO") ?? "",
                Cpf = obj.Value<string>("cpf") ?? obj.Value<string>("CPF") ?? "",
                Especialidade = obj.Value<string>("especialidade") ?? obj.Value<string>("ESPECIALIDADE") ?? ""
            };
            _byHash[hash] = entry;
        }

        public bool TryGet(string hash, out LaudoHashDbEntry? entry)
        {
            entry = null;
            if (string.IsNullOrWhiteSpace(hash)) return false;
            if (_byHash.TryGetValue(hash, out var e))
            {
                entry = e;
                return true;
            }
            return false;
        }
    }
}
