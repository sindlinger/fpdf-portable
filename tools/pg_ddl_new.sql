CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE EXTENSION IF NOT EXISTS unaccent;

DROP TABLE IF EXISTS document_features CASCADE;
DROP TABLE IF EXISTS features_catalog CASCADE;
DROP TABLE IF EXISTS page_objects CASCADE;
DROP TABLE IF EXISTS page_fonts CASCADE;
DROP TABLE IF EXISTS page_annotations CASCADE;
DROP TABLE IF EXISTS page_images CASCADE;
DROP TABLE IF EXISTS pages CASCADE;
DROP TABLE IF EXISTS documents CASCADE;
DROP TABLE IF EXISTS processes CASCADE;

CREATE TABLE processes (
  id SERIAL PRIMARY KEY,
  process_number TEXT UNIQUE,
  source TEXT,
  json JSONB,
  total_pages INTEGER,
  total_words INTEGER,
  total_chars INTEGER,
  total_images INTEGER,
  total_fonts INTEGER,
  scan_ratio NUMERIC(5,2),
  is_scanned BOOLEAN,
  is_encrypted BOOLEAN,
  perm_copy BOOLEAN,
  perm_print BOOLEAN,
  perm_annotate BOOLEAN,
  perm_fill_forms BOOLEAN,
  perm_extract BOOLEAN,
  perm_assemble BOOLEAN,
  perm_print_hq BOOLEAN,
  has_js BOOLEAN,
  has_embedded_files BOOLEAN,
  has_attachments BOOLEAN,
  has_multimedia BOOLEAN,
  has_forms BOOLEAN,
  meta_title TEXT,
  meta_author TEXT,
  meta_subject TEXT,
  meta_keywords TEXT,
  meta_creator TEXT,
  meta_producer TEXT,
  created_pdf TIMESTAMPTZ,
  modified_pdf TIMESTAMPTZ,
  doc_types JSONB,
  -- header/footer inferidos do PDF (1ª e última páginas)
  header_origin TEXT,
  header_title TEXT,
  header_subtitle TEXT,
  footer_signers TEXT[],
  footer_signed_at TIMESTAMPTZ,
  footer_signature_raw TEXT,
  created_at TIMESTAMPTZ DEFAULT now()
);

CREATE TABLE documents (
  id SERIAL PRIMARY KEY,
  process_id INTEGER REFERENCES processes(id) ON DELETE CASCADE,
  doc_key TEXT,
  doc_label_raw TEXT,
  doc_type TEXT,
  subtype TEXT,
  start_page INTEGER,
  end_page INTEGER,
  meta JSONB,
   total_pages INTEGER,
   total_words INTEGER,
   total_chars INTEGER,
   total_images INTEGER,
   total_fonts INTEGER,
   scan_ratio NUMERIC(5,2),
   has_forms BOOLEAN,
   has_annotations BOOLEAN,
  -- header/footer específicos do doc (1ª/última página do range)
  header_origin TEXT,
  header_title TEXT,
  header_subtitle TEXT,
  footer_signers TEXT[],
  footer_signed_at TIMESTAMPTZ,
  footer_signature_raw TEXT,
  created_at TIMESTAMPTZ DEFAULT now()
);

CREATE TABLE pages (
  id SERIAL PRIMARY KEY,
  document_id INTEGER REFERENCES documents(id) ON DELETE CASCADE,
  page_number INTEGER,
  text TEXT,
  header_virtual TEXT,
  footer_virtual TEXT,
  words INTEGER,
  chars INTEGER,
  is_scanned BOOLEAN,
  has_text BOOLEAN,
  image_count INTEGER,
  annotation_count INTEGER,
  has_form BOOLEAN,
  has_js BOOLEAN,
  font_count INTEGER,
  fonts TEXT[],
  has_money BOOLEAN,
  has_cpf BOOLEAN
);

CREATE TABLE page_images (
  id SERIAL PRIMARY KEY,
  page_id INTEGER REFERENCES pages(id) ON DELETE CASCADE,
  img_index INTEGER,
  name TEXT,
  width INTEGER,
  height INTEGER,
  color_space TEXT,
  compression TEXT,
  bits_per_component INTEGER,
  estimated_size BIGINT
);

CREATE TABLE page_annotations (
  id SERIAL PRIMARY KEY,
  page_id INTEGER REFERENCES pages(id) ON DELETE CASCADE,
  type TEXT,
  author TEXT,
  subject TEXT,
  contents TEXT,
  modification_date TIMESTAMPTZ,
  x NUMERIC,
  y NUMERIC
);

CREATE TABLE page_fonts (
  id SERIAL PRIMARY KEY,
  page_id INTEGER REFERENCES pages(id) ON DELETE CASCADE,
  name TEXT,
  base_font TEXT,
  font_type TEXT,
  size NUMERIC,
  is_embedded BOOLEAN,
  is_bold BOOLEAN,
  is_italic BOOLEAN,
  is_underline BOOLEAN,
  is_strikeout BOOLEAN,
  is_monospace BOOLEAN,
  is_serif BOOLEAN,
  is_sans BOOLEAN
);

CREATE TABLE page_objects (
  id SERIAL PRIMARY KEY,
  page_id INTEGER REFERENCES pages(id) ON DELETE CASCADE,
  obj_type TEXT,
  subtype TEXT,
  name TEXT,
  stream_size BIGINT,
  meta JSONB
);

CREATE TABLE bookmarks (
  id SERIAL PRIMARY KEY,
  process_id INTEGER REFERENCES processes(id) ON DELETE CASCADE,
  title TEXT,
  page_number INTEGER,
  level INTEGER
);

CREATE TABLE features_catalog (
  feature_key TEXT PRIMARY KEY,
  description TEXT,
  value_type TEXT
);

CREATE TABLE document_features (
  document_id INTEGER REFERENCES documents(id) ON DELETE CASCADE,
  feature_key TEXT REFERENCES features_catalog(feature_key) ON DELETE CASCADE,
  value_text TEXT,
  value_num DOUBLE PRECISION,
  value_bool BOOLEAN,
  PRIMARY KEY (document_id, feature_key)
);

-- FTS em pages
CREATE INDEX IF NOT EXISTS idx_pages_text_fts ON pages USING GIN (to_tsvector('portuguese', coalesce(text,'')));
CREATE INDEX IF NOT EXISTS idx_pages_doc ON pages(document_id, page_number);
CREATE INDEX IF NOT EXISTS idx_docs_process ON documents(process_id);
CREATE UNIQUE INDEX IF NOT EXISTS uq_process ON processes(process_number);

CREATE INDEX IF NOT EXISTS idx_page_images_page ON page_images(page_id);
CREATE INDEX IF NOT EXISTS idx_page_annotations_page ON page_annotations(page_id);
CREATE INDEX IF NOT EXISTS idx_page_fonts_page ON page_fonts(page_id);
CREATE INDEX IF NOT EXISTS idx_page_objects_page ON page_objects(page_id);
