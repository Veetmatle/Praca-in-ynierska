import { useState, useEffect, useCallback, useRef } from 'react';
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
  const [streamingMap, setStreamingMap] = useState({});
  const [streamingSessions, setStreamingSessions] = useState(new Set());
  const [pendingFiles, setPendingFiles] = useState([]);

  const activeStreamingText = streamingMap[activeSessionId] || '';
  const isActiveStreaming = streamingSessions.has(activeSessionId);
  const streamingSessionRef = useRef(null);

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
    chat.onChunk((sessionId, text) => {
      setStreamingMap((prev) => ({ ...prev, [sessionId]: (prev[sessionId] || '') + text }));
    });

    chat.onEnd((sessionId, error) => {
      setStreamingMap((prev) => {
        const next = { ...prev };
        delete next[sessionId];
        return next;
      });
      setStreamingSessions((prev) => {
        const next = new Set(prev);
        next.delete(sessionId);
        return next;
      });
      streamingSessionRef.current = null;
      if (error && sessionId === activeSessionId) {
        setMessages((prev) => [...prev, { id: Date.now(), role: 'Assistant', content: error }]);
      }
    });

    chat.onMessageSaved((msg) => {
      setMessages((prev) => {
        if (msg.role === 'User') {
          const withoutTemp = prev.filter((m) => !m._temp);
          return [...withoutTemp, msg];
        }
        if (prev.some((m) => m.id === msg.id)) return prev;
        return [...prev, msg];
      });
    });

    chat.onTitleUpdated(({ sessionPublicId, title }) => {
      setSessions((prev) =>
        prev.map((s) => (s.publicId === sessionPublicId ? { ...s, title } : s))
      );
      setActiveSession((prev) => (prev ? { ...prev, title } : prev));
    });

    chat.onError((msg) => {
      setMessages((prev) => [...prev, { id: Date.now(), role: 'Assistant', content: `[Błąd: ${msg}]` }]);
      const sid = streamingSessionRef.current;
      if (sid) {
        setStreamingSessions((prev) => {
          const next = new Set(prev);
          next.delete(sid);
          return next;
        });
      }
    });

    chat.onFile((file) => {
      setPendingFiles((prev) => {
        if (prev.some(f => f.fileName === file.fileName)) return prev;
        return [...prev, file];
      });
    });
  }, [chat, activeSessionId]);

  // Select session
  const handleSelectSession = async (publicId) => {
    setActiveSessionId(publicId);
    setPendingFiles([]);
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
  const handleSendMessage = (content, attachments = []) => {
    if (!activeSessionId) return;
    const displayContent = attachments?.length > 0
      ? `${content}\n📎 ${attachments.map(a => a.fileName).join(', ')}`
      : content;
    const tempMsg = { id: Date.now(), role: 'User', content: displayContent || content, _temp: true };
    setMessages((prev) => [...prev, tempMsg]);

    streamingSessionRef.current = activeSessionId;
    setStreamingSessions((prev) => new Set(prev).add(activeSessionId));
    setStreamingMap((prev) => ({ ...prev, [activeSessionId]: '' }));

    if (attachments?.length > 0 && chat.sendMessageWithAttachments) {
      chat.sendMessageWithAttachments(activeSessionId, content, attachments);
    } else {
      chat.sendMessage(activeSessionId, content);
    }
  };

  const handleTogglePin = async (publicId) => {
    try {
      await api.togglePin(publicId);
      await loadSessions();
    } catch (err) {
      console.error('Failed to toggle pin:', err);
    }
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
        streamingSessions={streamingSessions}
        onSelectSession={handleSelectSession}
        onNewSession={() => setShowNewSession(true)}
        onOpenSettings={() => setShowSettings(true)}
        onOpenAdmin={() => setShowAdmin(true)}
        onRefreshSessions={loadSessions}
        onTogglePin={handleTogglePin}
      />

      <ChatWindow
        session={activeSession}
        messages={messages}
        streamingText={activeStreamingText}
        isStreaming={isActiveStreaming}
        onSendMessage={handleSendMessage}
        pendingFiles={pendingFiles}
        onClearFiles={() => setPendingFiles([])}
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
