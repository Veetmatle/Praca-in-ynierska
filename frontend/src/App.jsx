import { useState, useEffect, useCallback } from 'react';
import { useAuth } from './contexts/AuthContext';
import { useChat } from './hooks/useChat';
import * as api from './services/api';
import LoginPage from './components/Auth/LoginPage';
import Sidebar from './components/Layout/Sidebar';
import ChatWindow from './components/Chat/ChatWindow';
import NewSessionDialog from './components/Chat/NewSessionDialog';
import SettingsPanel from './components/Settings/SettingsPanel';
import AdminPanel from './components/Admin/AdminPanel';

export default function App() {
  const { user, loading } = useAuth();
  const chat = useChat();

  const [sessions, setSessions] = useState([]);
  const [activeSessionId, setActiveSessionId] = useState(null);
  const [activeSession, setActiveSession] = useState(null);
  const [messages, setMessages] = useState([]);
  const [streamingText, setStreamingText] = useState('');

  const [showNewSession, setShowNewSession] = useState(false);
  const [showSettings, setShowSettings] = useState(false);
  const [showAdmin, setShowAdmin] = useState(false);

  // Load sessions
  const loadSessions = useCallback(async () => {
    try {
      const data = await api.getSessions();
      setSessions(data);
    } catch { /* ignore on auth fail */ }
  }, []);

  useEffect(() => {
    if (user) {
      loadSessions();
      chat.connect();
    }
    return () => { chat.disconnect(); };
  }, [user]);

  // SignalR event handlers
  useEffect(() => {
    chat.onChunk((chunk) => {
      setStreamingText((prev) => prev + chunk);
    });

    chat.onEnd((error) => {
      setStreamingText('');
      if (error) {
        setMessages((prev) => [...prev, { id: Date.now(), role: 'Assistant', content: error }]);
      }
    });

    chat.onMessageSaved((msg) => {
      setMessages((prev) => {
        const exists = prev.some((m) => m.id === msg.id);
        if (exists) return prev;
        return [...prev, msg];
      });
      setStreamingText('');
    });

    chat.onTitleUpdated(({ sessionPublicId, title }) => {
      setSessions((prev) =>
        prev.map((s) => (s.publicId === sessionPublicId ? { ...s, title } : s))
      );
      setActiveSession((prev) => (prev ? { ...prev, title } : prev));
    });

    chat.onError((msg) => {
      setMessages((prev) => [...prev, { id: Date.now(), role: 'Assistant', content: `[Błąd: ${msg}]` }]);
    });
  }, [chat]);

  // Select session
  const handleSelectSession = async (publicId) => {
    setActiveSessionId(publicId);
    setStreamingText('');
    try {
      const detail = await api.getSession(publicId);
      setActiveSession(detail);
      setMessages(detail.messages || []);
    } catch {
      setMessages([]);
    }
  };

  // Create session
  const handleCreateSession = async (title, category) => {
    try {
      const session = await api.createSession(title, category);
      setShowNewSession(false);
      await loadSessions();
      handleSelectSession(session.publicId);
    } catch (err) {
      console.error('Failed to create session:', err);
    }
  };

  // Send message
  const handleSendMessage = (content) => {
    if (!activeSessionId) return;

    // Optimistic UI: show user message immediately
    const tempMsg = { id: Date.now(), role: 'User', content };
    setMessages((prev) => [...prev, tempMsg]);
    setStreamingText('');

    chat.sendMessage(activeSessionId, content);
  };

  if (loading) {
    return (
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', height: '100vh', color: 'var(--text-muted)' }}>
        Ładowanie...
      </div>
    );
  }

  if (!user) {
    return <LoginPage />;
  }

  return (
    <div className="app-layout">
      <Sidebar
        sessions={sessions}
        activeSessionId={activeSessionId}
        onSelectSession={handleSelectSession}
        onNewSession={() => setShowNewSession(true)}
        onOpenSettings={() => setShowSettings(true)}
        onOpenAdmin={() => setShowAdmin(true)}
        onRefreshSessions={loadSessions}
      />

      <ChatWindow
        session={activeSession}
        messages={messages}
        streamingText={streamingText}
        isStreaming={chat.isStreaming}
        onSendMessage={handleSendMessage}
      />

      {showNewSession && (
        <NewSessionDialog
          onClose={() => setShowNewSession(false)}
          onCreate={handleCreateSession}
        />
      )}

      {showSettings && <SettingsPanel onClose={() => setShowSettings(false)} />}
      {showAdmin && <AdminPanel onClose={() => setShowAdmin(false)} />}
    </div>
  );
}
