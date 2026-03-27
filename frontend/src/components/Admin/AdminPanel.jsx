import { useState, useEffect } from 'react';
import { X, UserPlus, Trash2, RotateCcw } from 'lucide-react';
import * as api from '../../services/api';

export default function AdminPanel({ onClose }) {
  const [users, setUsers] = useState([]);
  const [showDeleted, setShowDeleted] = useState(false);
  const [showCreate, setShowCreate] = useState(false);
  const [createForm, setCreateForm] = useState({ username: '', displayName: '', password: '' });
  const [error, setError] = useState('');

  const loadUsers = () => {
    api.getUsers(showDeleted).then(setUsers).catch(() => {});
  };

  useEffect(() => { loadUsers(); }, [showDeleted]);

  const handleCreate = async () => {
    setError('');
    try {
      await api.createUser(createForm.username, createForm.displayName, createForm.password);
      setCreateForm({ username: '', displayName: '', password: '' });
      setShowCreate(false);
      loadUsers();
    } catch (err) {
      setError(err.message);
    }
  };

  const handleDelete = async (publicId, username) => {
    if (!confirm(`Czy na pewno chcesz zablokować użytkownika ${username}?`)) return;
    await api.deleteUser(publicId);
    loadUsers();
  };

  const handleRestore = async (publicId) => {
    await api.restoreUser(publicId);
    loadUsers();
  };

  return (
    <div className="settings-overlay" onClick={(e) => e.target === e.currentTarget && onClose()}>
      <div className="settings-panel" style={{ width: 500 }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 20 }}>
          <h2>Panel Admina</h2>
          <button className="icon-btn" onClick={onClose}><X size={20} /></button>
        </div>

        <div style={{ display: 'flex', gap: 8, marginBottom: 16 }}>
          <button
            className="btn btn-primary"
            style={{ display: 'flex', alignItems: 'center', gap: 6 }}
            onClick={() => setShowCreate(!showCreate)}
          >
            <UserPlus size={14} /> Nowy użytkownik
          </button>
          <label style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 13, color: 'var(--text-secondary)' }}>
            <input type="checkbox" checked={showDeleted} onChange={(e) => setShowDeleted(e.target.checked)} />
            Pokaż zablokowanych
          </label>
        </div>

        {showCreate && (
          <div style={{ background: 'var(--bg-secondary)', padding: 16, borderRadius: 8, marginBottom: 16 }}>
            {error && <div className="login-error">{error}</div>}
            <div className="form-group">
              <label>Login</label>
              <input value={createForm.username} onChange={(e) => setCreateForm((p) => ({ ...p, username: e.target.value }))} />
            </div>
            <div className="form-group">
              <label>Imię i nazwisko</label>
              <input value={createForm.displayName} onChange={(e) => setCreateForm((p) => ({ ...p, displayName: e.target.value }))} />
            </div>
            <div className="form-group">
              <label>Hasło początkowe</label>
              <input type="password" value={createForm.password} onChange={(e) => setCreateForm((p) => ({ ...p, password: e.target.value }))} />
            </div>
            <button className="btn btn-primary" onClick={handleCreate}>Utwórz konto</button>
          </div>
        )}

        <table className="admin-table">
          <thead>
            <tr>
              <th>Użytkownik</th>
              <th>Rola</th>
              <th>Utworzony</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {users.map((u) => (
              <tr key={u.publicId} style={{ opacity: u.isDeleted ? 0.5 : 1 }}>
                <td>
                  <div style={{ fontWeight: 500 }}>{u.displayName}</div>
                  <div style={{ fontSize: 11, color: 'var(--text-muted)' }}>{u.username}</div>
                </td>
                <td>{u.role}</td>
                <td style={{ fontSize: 12 }}>
                  {new Date(u.createdAt).toLocaleDateString('pl-PL')}
                </td>
                <td>
                  {u.role !== 'Admin' && !u.isDeleted && (
                    <button className="icon-btn" onClick={() => handleDelete(u.publicId, u.username)} title="Zablokuj">
                      <Trash2 size={14} />
                    </button>
                  )}
                  {u.isDeleted && (
                    <button className="icon-btn" onClick={() => handleRestore(u.publicId)} title="Przywróć">
                      <RotateCcw size={14} />
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
