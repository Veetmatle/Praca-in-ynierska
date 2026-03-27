import { useState } from 'react';
import { X } from 'lucide-react';

const CATEGORIES = [
  { key: 'Gemini', label: 'Gemini AI', desc: 'Rozmowy z Google Gemini — pytania, analiza, pomoc z zadaniami.' },
  { key: 'OpenClaw', label: 'OpenClaw (Claude)', desc: 'Agent AI — wykonywanie kodu, generowanie plików, analiza danych.' },
  { key: 'UniScraper', label: 'Uczelnia', desc: 'Pytania o plany zajęć, harmonogramy, dokumenty ze strony uczelni.' },
];

export default function NewSessionDialog({ onClose, onCreate }) {
  const [title, setTitle] = useState('');
  const [category, setCategory] = useState('Gemini');

  const handleCreate = () => {
    const sessionTitle = title.trim() || 'Nowa rozmowa';
    onCreate(sessionTitle, category);
  };

  return (
    <div className="settings-overlay" onClick={(e) => e.target === e.currentTarget && onClose()}>
      <div className="settings-panel" style={{ width: 420 }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 20 }}>
          <h2>Nowa rozmowa</h2>
          <button className="icon-btn" onClick={onClose}><X size={20} /></button>
        </div>

        <div className="form-group">
          <label>Tytuł (opcjonalny)</label>
          <input
            value={title}
            onChange={(e) => setTitle(e.target.value)}
            placeholder="Automatycznie z pierwszej wiadomości..."
          />
        </div>

        <div className="settings-section">
          <h3>Wybierz asystenta</h3>
          {CATEGORIES.map((cat) => (
            <div
              key={cat.key}
              onClick={() => setCategory(cat.key)}
              style={{
                padding: '12px 14px',
                borderRadius: 8,
                cursor: 'pointer',
                marginBottom: 8,
                border: `2px solid ${category === cat.key ? 'var(--accent)' : 'var(--border)'}`,
                background: category === cat.key ? 'rgba(139,115,85,0.06)' : 'transparent',
                transition: 'all 0.15s',
              }}
            >
              <div style={{ fontWeight: 600, fontSize: 14 }}>{cat.label}</div>
              <div style={{ fontSize: 12, color: 'var(--text-muted)', marginTop: 2 }}>{cat.desc}</div>
            </div>
          ))}
        </div>

        <button className="btn btn-primary" style={{ width: '100%' }} onClick={handleCreate}>
          Rozpocznij rozmowę
        </button>
      </div>
    </div>
  );
}
