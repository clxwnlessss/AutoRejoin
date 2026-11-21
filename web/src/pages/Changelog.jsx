import { useEffect, useState } from 'react';
import { useLang } from '../i18n.jsx';
import { releases, REPO } from '../api.js';

// Tiny Markdown → React for release notes: headings, lists, bold, inline code, links, rules. No HTML from the source.
function inline(text, key) {
  const parts = [];
  const re = /(\*\*[^*]+\*\*|`[^`]+`|\[[^\]]+\]\(https?:\/\/[^)\s]+\)|\*[^*]+\*)/g;
  let last = 0, m, i = 0;
  while ((m = re.exec(text))) {
    if (m.index > last) parts.push(text.slice(last, m.index));
    const s = m[0];
    if (s.startsWith('**')) parts.push(<strong key={`${key}-${i++}`}>{s.slice(2, -2)}</strong>);
    else if (s.startsWith('`')) parts.push(<code key={`${key}-${i++}`}>{s.slice(1, -1)}</code>);
    else if (s.startsWith('[')) { const mm = s.match(/^\[([^\]]+)\]\(([^)]+)\)$/); parts.push(<a key={`${key}-${i++}`} href={mm[2]} target="_blank" rel="noreferrer">{mm[1]}</a>); }
    else parts.push(<em key={`${key}-${i++}`}>{s.slice(1, -1)}</em>);
    last = m.index + s.length;
  }
  if (last < text.length) parts.push(text.slice(last));
  return parts;
}

function Markdown({ text }) {
  const out = [];
  let list = null;
  const flush = () => { if (list) { out.push(<ul key={`u${out.length}`}>{list}</ul>); list = null; } };
  String(text || '').split(/\r?\n/).forEach((raw, n) => {
    const line = raw.trimEnd();
    if (/^\s*[-*]\s+/.test(line)) { (list = list || []).push(<li key={n}>{inline(line.replace(/^\s*[-*]\s+/, ''), n)}</li>); return; }
    if (/^\s*\d+\.\s+/.test(line)) { (list = list || []).push(<li key={n}>{inline(line.replace(/^\s*\d+\.\s+/, ''), n)}</li>); return; }
    flush();
    if (!line.trim()) return;
    if (/^---+$/.test(line.trim())) out.push(<hr key={n} />);
    else if (line.startsWith('### ')) out.push(<h3 key={n}>{inline(line.slice(4), n)}</h3>);
    else if (line.startsWith('## ') || line.startsWith('# ')) out.push(<h2 key={n}>{inline(line.replace(/^#+\s/, ''), n)}</h2>);
    else out.push(<p key={n}>{inline(line, n)}</p>);
  });
  flush();
  return <div className="md">{out}</div>;
}

// Release notes are written EN first, then RU after a "---" line: show the half for the current language.
function pickLanguage(body, lang) {
  const parts = String(body || '').split(/\r?\n---+\r?\n/);
  if (parts.length < 2) return body;
  const ratio = (p) => { const cy = (p.match(/[а-яё]/gi) || []).length; const la = (p.match(/[a-z]/gi) || []).length; return cy / Math.max(1, cy + la); };
  const sorted = parts.slice().sort((a, b) => ratio(b) - ratio(a));
  return lang === 'ru' ? sorted[0] : sorted[sorted.length - 1];
}

export default function Changelog() {
  const { t, lang } = useLang();
  const [list, setList] = useState(null);
  const [err, setErr] = useState(false);
  useEffect(() => { releases().then(setList).catch(() => setErr(true)); }, []);
  return (
    <div className="wrap page">
      <h1>{t('Обновления', 'Changelog')}</h1>
      <p className="lead">{t('Программа обновляется сама — здесь видно, что нового.', 'The app updates itself — here’s what’s new.')}</p>
      {err && <div className="error">{t('GitHub не ответил. ', 'GitHub did not answer. ')}<a href={`https://github.com/${REPO}/releases`}>{t('Открыть релизы на GitHub', 'Open releases on GitHub')}</a></div>}
      {!list && !err && <div className="center" style={{ padding: 30 }}><span className="spinner" /></div>}
      <div style={{ display: 'grid', gap: 14 }}>
        {(list || []).map((r) => (
          <article className="card" key={r.id}>
            <div className="row between">
              <span className="badge b-cyan">{r.tag_name}</span>
              <span className="muted" style={{ fontSize: 14 }}>{new Date(r.published_at).toLocaleDateString(lang === 'ru' ? 'ru-RU' : 'en-US', { day: 'numeric', month: 'long', year: 'numeric' })}</span>
            </div>
            <Markdown text={pickLanguage(r.body, lang)} />
          </article>
        ))}
      </div>
    </div>
  );
}
