import { useState, useEffect } from 'react';
import { Settings, Plus, LogOut, Shield, MessageSquare, Bot, GraduationCap } from 'lucide-react';
import { useAuth } from '../../contexts/AuthContext';
import * as api from '../../services/api';

const CATEGORIES = [
  { key: null, label: 'Wszystkie', icon: MessageSquare },
  { key: 'Gemini', label: 'Gemini', icon: MessageSquare },
  { key: 'OpenClaw', label: 'OpenClaw', icon: Bot },
  { key: 'UniScraper', label: 'Uczelnia', icon: GraduationCap },
];

export default function Sidebar({
  sessions,
  activeSessionId,
  onSelectSession,
  onNewSession,
  onOpenSettings,
  onOpenAdmin,
  onRefreshSessions,
}) {
  const { user, logout } = useAuth();
  const [filter, setFilter] = useState(null);

  const filteredSessions = filter
    ? sessions.filter((s) => s.category === filter)
    : sessions;

  const formatDate = (dateStr) => {
    const d = new Date(dateStr);
    const now = new Date();
    const diff = now - d;
    if (diff < 86400000) {
      return d.toLocaleTimeString('pl-PL', { hour: '2-digit', minute: '2-digit' });
    }
    return d.toLocaleDateString('pl-PL', { day: 'numeric', month: 'short' });
  };

  const getCategoryIcon = (category) => {
    switch (category) {
      case 'Gemini': return '✦';
      case 'OpenClaw': return '⚡';
      case 'UniScraper': return '🎓';
      default: return '💬';
    }
  };

  return (
    <div className="sidebar">
      <div className="sidebar-header">
        <h1>StudentApp</h1>
        <p className="subtitle">Asystent AI</p>
      </div>

      <div className="sidebar-actions">
        {CATEGORIES.map((cat) => (
          <button
            key={cat.label}
            className={filter === cat.key ? 'active' : ''}
            onClick={() => setFilter(cat.key)}
            title={cat.label}
          >
            {cat.label}
          </button>
        ))}
      </div>

      <div style={{ padding: '0 8px 8px' }}>
        <button
          className="btn btn-primary"
          style={{ width: '100%', display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 6 }}
          onClick={onNewSession}
        >
          <Plus size={14} /> Nowa rozmowa
        </button>
      </div>

      <div className="session-list">
        {filteredSessions.length === 0 && (
          <p style={{ textAlign: 'center', color: 'var(--text-muted)', fontSize: 13, padding: 20 }}>
            Brak rozmów
          </p>
        )}
        {filteredSessions.map((session) => (
          <div
            key={session.publicId}
            className={`session-item ${activeSessionId === session.publicId ? 'active' : ''}`}
            onClick={() => onSelectSession(session.publicId)}
          >
            <div className="title">
              {getCategoryIcon(session.category)} {session.title}
            </div>
            <div className="meta">
              {session.category} · {formatDate(session.updatedAt)} · {session.messageCount} wiad.
            </div>
          </div>
        ))}
      </div>

      <div className="sidebar-footer">
        <div className="user-info">
          <div className="user-name">{user?.displayName}</div>
          <div className="user-role">{user?.role === 'Admin' ? 'Administrator' : 'Student'}</div>
        </div>

        {user?.role === 'Admin' && (
          <button className="icon-btn" onClick={onOpenAdmin} title="Panel admina">
            <Shield size={18} />
          </button>
        )}
        <button className="icon-btn" onClick={onOpenSettings} title="Ustawienia">
          <Settings size={18} />
        </button>
        <button className="icon-btn" onClick={logout} title="Wyloguj">
          <LogOut size={18} />
        </button>
      </div>
    </div>
  );
}
