// src/Client/src/main.tsx
import { StrictMode, useEffect, type ReactNode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import './styles/tokens.css'
import './App.css'
import App from './App.tsx'
import { AccountProvider } from './context/AccountContext'
import { TooltipHost } from './components/Tooltip'

const DESIGN_W = 1280
const DESIGN_H = 720

document.addEventListener('contextmenu', (e) => {
  e.preventDefault()
})

function AppStage({ children }: { children: ReactNode }) {
  useEffect(() => {
    function update() {
      const scale = Math.min(window.innerWidth / DESIGN_W, window.innerHeight / DESIGN_H)
      document.documentElement.style.setProperty('--app-scale', String(scale))
    }
    update()
    window.addEventListener('resize', update)
    return () => window.removeEventListener('resize', update)
  }, [])
  return <div className="app-stage">{children}</div>
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <AccountProvider>
      <TooltipHost>
        <AppStage>
          <App />
        </AppStage>
      </TooltipHost>
    </AccountProvider>
  </StrictMode>,
)
