import { createContext, useContext, useState, useEffect, useCallback, useRef } from 'react';
import * as api from '../services/api';

const AuthContext = createContext(null);

export function AuthProvider({ children }) {
  const [user, setUser] = useState(null);
  const [loading, setLoading] = useState(true);

  const handleLogout = useCallback(async () => {
    await api.logout();
    setUser(null);
  }, []);

  // Try silent refresh on mount
  useEffect(() => {
    api.setOnAuthFailure(() => setUser(null));
    
    api.refreshToken().then((data) => {
      if (data) {
        setUser({
          username: data.username,
          displayName: data.displayName,
          role: data.role,
        });
      }
    }).finally(() => setLoading(false));

  }, []);

  // Periodic token refresh — separate useEffect to avoid stale closure
  const userRef = useRef(user);
  useEffect(() => {
    userRef.current = user;
  }, [user]);

  useEffect(() => {
    const interval = setInterval(() => {
      if (userRef.current) {
        api.refreshToken().then((data) => {
          if (!data && userRef.current) {
            // Refresh failed — token expired, force logout
            setUser(null);
          }
        });
      }
    }, 12 * 60 * 1000);

    return () => clearInterval(interval);
  }, []);

  const handleLogin = async (username, password) => {
    const data = await api.login(username, password);
    setUser({
      username: data.username,
      displayName: data.displayName,
      role: data.role,
    });
    return data;
  };

  return (
    <AuthContext.Provider value={{ user, loading, login: handleLogin, logout: handleLogout }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within AuthProvider');
  return ctx;
}
