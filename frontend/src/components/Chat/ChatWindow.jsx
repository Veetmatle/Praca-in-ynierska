import { useState, useRef, useEffect } from 'react';
import { Send, Download } from 'lucide-react';
import ReactMarkdown from 'react-markdown';

export default function ChatWindow({
  session,
  messages,
  streamingText,
  isStreaming,
  onSendMessage,
  pendingFiles = [],
  onClearFiles,
}) {
  const [input, setInput] = useState('');
  const messagesEndRef = useRef(null);
  const textareaRef = useRef(null);

  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages, streamingText]);

  const handleSend = () => {
    const text = input.trim();
    if (!text || isStreaming) return;
    onSendMessage(text);
    setInput('');
    if (textareaRef.current) textareaRef.current.style.height = 'auto';
  };

  const handleKeyDown = (e) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      handleSend();
    }
  };

  const handleDownloadFile = (file) => {
    const byteChars = atob(file.base64Data);
    const byteNumbers = new Array(byteChars.length);
    for (let i = 0; i < byteChars.length; i++) {
      byteNumbers[i] = byteChars.charCodeAt(i);
    }
    const blob = new Blob([new Uint8Array(byteNumbers)], { type: file.mimeType });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = file.fileName;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
  };

  const handleTextareaChange = (e) => {
    setInput(e.target.value);
    e.target.style.height = 'auto';
    e.target.style.height = Math.min(e.target.scrollHeight, 120) + 'px';
  };

  if (!session) {
    return (
      <div className="chat-area">
        <div className="empty-state">
          <h3>Wybierz lub utwórz rozmowę</h3>
          <p>Użyj panelu po lewej stronie, aby wybrać istniejącą sesję czatu lub rozpocząć nową rozmowę.</p>
        </div>
      </div>
    );
  }

  return (
    <div className="chat-area">
      <div className="chat-header">
        <h2>{session.title}</h2>
        <span className="badge">{session.category}</span>
      </div>

      <div className="messages-container">
        {messages.map((msg, idx) => (
          <div key={msg.id || idx} className={`message-bubble ${msg.role.toLowerCase()}`}>
            {msg.role === 'Assistant' ? (
              <ReactMarkdown>{msg.content}</ReactMarkdown>
            ) : (
              msg.content
            )}
          </div>
        ))}

        {isStreaming && streamingText && (
          <div className="message-bubble assistant">
            <ReactMarkdown>{streamingText}</ReactMarkdown>
          </div>
        )}

        {isStreaming && !streamingText && (
          <div className="typing-indicator">
            <span /><span /><span />
          </div>
        )}

        {pendingFiles.length > 0 && (
          <div className="files-download-container">
            {pendingFiles.map((file, idx) => (
              <div key={idx} className="file-download-card">
                <div className="file-info">
                  <span className="file-icon">📄</span>
                  <div>
                    <div className="file-name">{file.fileName}</div>
                    <div className="file-meta">
                      {(file.sizeBytes / 1024).toFixed(0)} KB
                    </div>
                  </div>
                </div>
                <button className="btn btn-primary btn-sm" onClick={() => handleDownloadFile(file)}
                  style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
                  <Download size={14} /> Pobierz
                </button>
              </div>
            ))}
          </div>
        )}

        <div ref={messagesEndRef} />
      </div>

      <div className="chat-input-area">
        <div className="chat-input-wrapper">
          <textarea
            ref={textareaRef}
            value={input}
            onChange={handleTextareaChange}
            onKeyDown={handleKeyDown}
            placeholder="Napisz wiadomość..."
            rows={1}
            disabled={isStreaming}
          />
          <button
            className="send-btn"
            onClick={handleSend}
            disabled={!input.trim() || isStreaming}
          >
            <Send size={16} />
          </button>
        </div>
      </div>
    </div>
  );
}
