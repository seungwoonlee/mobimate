import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import './styles/tokens.css';
import './styles/palettes.css';
import './styles/base.css';
import './styles/app.css';
import { App } from './App';
import { installErrorReporting } from './lib/errorReport';

installErrorReporting();   // 처리되지 않은 오류를 PC 서버 로그로 (FR-ST-03)

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
