import { useState, useEffect } from 'react';
import { X, Check } from 'lucide-react';
import * as api from '../../services/api';

export default function SettingsPanel({ onClose }) {
  const [config, setConfig] = useState(null);
  const [form, setForm] = useState({});
  const [passwordForm, setPasswordForm] = useState({ current: '', newPw: '', confirm: '' });
  const [saving, setSaving] = useState(false);
  const [message, setMessage] = useState('');

  useEffect(() => {
    api.getConfig().then((data) => {
      if (data) {
        setConfig(data);
        setForm({
          universityName: data.universityName || '',
          faculty: data.faculty || '',
          fieldOfStudy: data.fieldOfStudy || '',
          academicYear: data.academicYear || '',
          studyYear: data.studyYear || '',
          deanGroup: data.deanGroup || '',
          geminiModel: data.geminiModel || 'gemini-2.5-flash',
          anthropicModel: data.anthropicModel || 'claude-sonnet-4-20250514',
          geminiApiKey: '',
          anthropicApiKey: '',
        });
      }
    });
  }, []);

  const handleSave = async () => {
    setSaving(true);
    setMessage('');
    try {
      const payload = { ...form };
      if (!payload.geminiApiKey) delete payload.geminiApiKey;
      if (!payload.anthropicApiKey) delete payload.anthropicApiKey;
      if (payload.studyYear === '') payload.studyYear = null;
      else payload.studyYear = parseInt(payload.studyYear) || null;

      const updated = await api.updateConfig(payload);
      setConfig(updated);
      setForm((prev) => ({ ...prev, geminiApiKey: '', anthropicApiKey: '' }));
      setMessage('Konfiguracja zapisana');
    } catch {
      setMessage('Błąd zapisu konfiguracji');
    } finally {
      setSaving(false);
    }
  };

  const handlePasswordChange = async () => {
    if (passwordForm.newPw !== passwordForm.confirm) {
      setMessage('Nowe hasła nie są identyczne');
      return;
    }
    try {
      const ok = await api.changePassword(passwordForm.current, passwordForm.newPw);
      setMessage(ok ? 'Hasło zmienione' : 'Nieprawidłowe obecne hasło');
      if (ok) setPasswordForm({ current: '', newPw: '', confirm: '' });
    } catch {
      setMessage('Błąd zmiany hasła');
    }
  };

  const updateForm = (key, value) => setForm((prev) => ({ ...prev, [key]: value }));

  return (
    <div className="settings-overlay" onClick={(e) => e.target === e.currentTarget && onClose()}>
      <div className="settings-panel">
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 20 }}>
          <h2>Ustawienia</h2>
          <button className="icon-btn" onClick={onClose}><X size={20} /></button>
        </div>

        {message && (
          <div style={{
            padding: '8px 12px', borderRadius: 6, marginBottom: 16, fontSize: 13,
            background: message.includes('Błąd') || message.includes('Nieprawidłowe')
              ? 'rgba(196,78,61,0.08)' : 'rgba(90,138,94,0.1)',
            color: message.includes('Błąd') || message.includes('Nieprawidłowe')
              ? 'var(--accent-danger)' : 'var(--accent-success)',
          }}>
            {message}
          </div>
        )}

        <div className="settings-section">
          <h3>Klucze API</h3>
          <div className="form-group">
            <label>Gemini API Key</label>
            <input
              type="password"
              value={form.geminiApiKey || ''}
              onChange={(e) => updateForm('geminiApiKey', e.target.value)}
              placeholder="Wklej nowy klucz..."
            />
            <p className={`key-status ${config?.hasGeminiKey ? 'configured' : 'missing'}`}>
              {config?.hasGeminiKey ? '✓ Klucz skonfigurowany' : '⚠ Brak klucza'}
            </p>
          </div>
          <div className="form-group">
            <label>Anthropic API Key</label>
            <input
              type="password"
              value={form.anthropicApiKey || ''}
              onChange={(e) => updateForm('anthropicApiKey', e.target.value)}
              placeholder="Wklej nowy klucz..."
            />
            <p className={`key-status ${config?.hasAnthropicKey ? 'configured' : 'missing'}`}>
              {config?.hasAnthropicKey ? '✓ Klucz skonfigurowany' : '⚠ Brak klucza'}
            </p>
          </div>
        </div>

        <div className="settings-section">
          <h3>Uczelnia</h3>
          <div className="form-group">
            <label>Nazwa uczelni</label>
            <input
              list="university-list"
              value={form.universityName || ''}
              onChange={(e) => updateForm('universityName', e.target.value)}
              placeholder="Zacznij pisać nazwę uczelni..."
            />
            <datalist id="university-list">
              <option value="Politechnika Krakowska" />
              <option value="Akademia Górniczo-Hutnicza (AGH)" />
              <option value="Uniwersytet Jagielloński" />
              <option value="Politechnika Warszawska" />
              <option value="Politechnika Wrocławska" />
              <option value="Politechnika Gdańska" />
              <option value="Politechnika Śląska" />
              <option value="Politechnika Łódzka" />
              <option value="Politechnika Poznańska" />
              <option value="Politechnika Rzeszowska" />
              <option value="Uniwersytet Warszawski" />
              <option value="Uniwersytet Wrocławski" />
              <option value="Uniwersytet im. Adama Mickiewicza (UAM)" />
              <option value="Uniwersytet Gdański" />
              <option value="Uniwersytet Śląski" />
              <option value="Uniwersytet Łódzki" />
              <option value="Uniwersytet Mikołaja Kopernika (UMK)" />
              <option value="Uniwersytet Marii Curie-Skłodowskiej (UMCS)" />
              <option value="Uniwersytet Ekonomiczny w Krakowie" />
              <option value="Uniwersytet Ekonomiczny we Wrocławiu" />
              <option value="Uniwersytet Ekonomiczny w Poznaniu" />
              <option value="Uniwersytet Ekonomiczny w Katowicach" />
              <option value="Szkoła Główna Handlowa (SGH)" />
              <option value="Wojskowa Akademia Techniczna (WAT)" />
              <option value="Uniwersytet Technologiczno-Przyrodniczy w Bydgoszczy" />
            </datalist>
            <p className="hint">Wpisz nazwę lub wybierz z listy. Możesz wpisać dowolną.</p>
          </div>
          <div className="form-group">
            <label>Wydział</label>
            <input value={form.faculty || ''} onChange={(e) => updateForm('faculty', e.target.value)} placeholder="np. WIiT" />
          </div>
          <div className="form-group">
            <label>Kierunek studiów</label>
            <input value={form.fieldOfStudy || ''} onChange={(e) => updateForm('fieldOfStudy', e.target.value)} placeholder="np. Informatyka" />
          </div>
          <div style={{ display: 'flex', gap: 10 }}>
            <div className="form-group" style={{ flex: 1 }}>
              <label>Rok akademicki</label>
              <input value={form.academicYear || ''} onChange={(e) => updateForm('academicYear', e.target.value)} placeholder="2025/2026" />
            </div>
            <div className="form-group" style={{ flex: 1 }}>
              <label>Rok studiów</label>
              <input type="number" min="1" max="5" value={form.studyYear || ''} onChange={(e) => updateForm('studyYear', e.target.value)} />
            </div>
          </div>
          <div className="form-group">
            <label>Grupa dziekańska</label>
            <input value={form.deanGroup || ''} onChange={(e) => updateForm('deanGroup', e.target.value)} placeholder="np. 1a" />
          </div>
        </div>

        <div className="settings-section">
          <h3>Modele AI</h3>
          <div className="form-group">
            <label>Model Gemini</label>
            <select value={form.geminiModel || ''} onChange={(e) => updateForm('geminiModel', e.target.value)}>
              <optgroup label="Gemini 2.5">
                <option value="gemini-2.5-flash">Gemini 2.5 Flash</option>
                <option value="gemini-2.5-pro">Gemini 2.5 Pro</option>
              </optgroup>
              <optgroup label="Gemini 2.0">
                <option value="gemini-2.0-flash">Gemini 2.0 Flash</option>
                <option value="gemini-2.0-flash-lite">Gemini 2.0 Flash Lite</option>
              </optgroup>
              <optgroup label="Gemini 1.5">
                <option value="gemini-1.5-flash">Gemini 1.5 Flash</option>
                <option value="gemini-1.5-pro">Gemini 1.5 Pro</option>
              </optgroup>
            </select>
          </div>
          <div className="form-group">
            <label>Model Anthropic</label>
            <select value={form.anthropicModel || ''} onChange={(e) => updateForm('anthropicModel', e.target.value)}>
              <optgroup label="Claude 4">
                <option value="claude-sonnet-4-20250514">Claude Sonnet 4</option>
                <option value="claude-opus-4-20250514">Claude Opus 4</option>
              </optgroup>
              <optgroup label="Claude 3.7">
                <option value="claude-3-7-sonnet-20250219">Claude 3.7 Sonnet</option>
              </optgroup>
              <optgroup label="Claude 3.5">
                <option value="claude-3-5-sonnet-20241022">Claude 3.5 Sonnet (v2)</option>
                <option value="claude-3-5-haiku-20241022">Claude 3.5 Haiku</option>
              </optgroup>
            </select>
          </div>
        </div>

        <button className="btn btn-primary" style={{ width: '100%', marginBottom: 24 }} onClick={handleSave} disabled={saving}>
          {saving ? 'Zapisywanie...' : 'Zapisz konfigurację'}
        </button>

        <div className="settings-section">
          <h3>Zmiana hasła</h3>
          <div className="form-group">
            <label>Obecne hasło</label>
            <input type="password" value={passwordForm.current} onChange={(e) => setPasswordForm((p) => ({ ...p, current: e.target.value }))} />
          </div>
          <div className="form-group">
            <label>Nowe hasło</label>
            <input type="password" value={passwordForm.newPw} onChange={(e) => setPasswordForm((p) => ({ ...p, newPw: e.target.value }))} />
          </div>
          <div className="form-group">
            <label>Potwierdź nowe hasło</label>
            <input type="password" value={passwordForm.confirm} onChange={(e) => setPasswordForm((p) => ({ ...p, confirm: e.target.value }))} />
          </div>
          <button className="btn btn-secondary" onClick={handlePasswordChange}>Zmień hasło</button>
        </div>
      </div>
    </div>
  );
}
