import { useState, useRef, useCallback, useEffect } from 'react';
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { getAccessToken } from '../services/api';

/**
 * Custom hook for SignalR chat streaming.
 * Manages connection lifecycle, message streaming, and reconnection.
 */
export function useChat() {
  const [isConnected, setIsConnected] = useState(false);
  const connectionRef = useRef(null);
  const onChunkRef = useRef(null);
  const onEndRef = useRef(null);
  const onMessageSavedRef = useRef(null);
  const onTitleUpdatedRef = useRef(null);
  const onErrorRef = useRef(null);
  const onFileRef = useRef(null);

  const connect = useCallback(async () => {
    if (connectionRef.current) return;

    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/chat', { accessTokenFactory: () => getAccessToken() })
      .withAutomaticReconnect([0, 2000, 5000, 10000])
      .configureLogging(LogLevel.Warning)
      .build();

    connection.on('StreamChunk', (data) => {
      if (typeof data === 'object' && data.sessionId) {
        onChunkRef.current?.(data.sessionId, data.text);
      } else {
        // Fallback for old format (plain string)
        onChunkRef.current?.(null, data);
      }
    });

    connection.on('StreamEnd', (data) => {
      if (typeof data === 'object' && data.sessionId !== undefined) {
        onEndRef.current?.(data.sessionId, data.error);
      } else {
        // Fallback for old format (plain string or null)
        onEndRef.current?.(null, data);
      }
    });

    connection.on('MessageSaved', (msg) => {
      onMessageSavedRef.current?.(msg);
    });

    connection.on('SessionTitleUpdated', (data) => {
      onTitleUpdatedRef.current?.(data);
    });

    connection.on('Error', (msg) => {
      onErrorRef.current?.(msg);
    });

    connection.on('FileAvailable', (file) => {
      onFileRef.current?.(file);
    });

    connection.onreconnecting(() => setIsConnected(false));
    connection.onreconnected(() => setIsConnected(true));
    connection.onclose(() => {
      setIsConnected(false);
      connectionRef.current = null;
    });

    try {
      await connection.start();
      connectionRef.current = connection;
      setIsConnected(true);
    } catch (err) {
      console.error('SignalR connection failed:', err);
    }
  }, []);

  const disconnect = useCallback(async () => {
    if (connectionRef.current) {
      await connectionRef.current.stop();
      connectionRef.current = null;
      setIsConnected(false);
    }
  }, []);

  const sendMessage = useCallback(async (sessionPublicId, content) => {
    if (!connectionRef.current) return;
    try {
      await connectionRef.current.invoke('SendMessage', sessionPublicId, content);
    } catch (err) {
      onErrorRef.current?.(err.message);
    }
  }, []);

  useEffect(() => {
    return () => { disconnect(); };
  }, [disconnect]);

  return {
    isConnected,
    connect,
    disconnect,
    sendMessage,
    onChunk: (fn) => { onChunkRef.current = fn; },
    onEnd: (fn) => { onEndRef.current = fn; },
    onMessageSaved: (fn) => { onMessageSavedRef.current = fn; },
    onTitleUpdated: (fn) => { onTitleUpdatedRef.current = fn; },
    onError: (fn) => { onErrorRef.current = fn; },
    onFile: (fn) => { onFileRef.current = fn; },
  };
}
