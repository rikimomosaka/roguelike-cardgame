// src/Client/src/main.tsx
import { StrictMode, useEffect, type ReactNode } from 'react'
import { createRoot } from 'react-dom/client'
import './styles/fonts.css'
import './index.css'
import './styles/tokens.css'
import './App.css'
import App from './App.tsx'
import { AccountProvider } from './context/AccountContext'
import { TooltipHost } from './components/Tooltip'
import { AudioProvider } from './audio'
import { PresentationProvider } from './presentation'

const DESIGN_W = 1280
const DESIGN_H = 720

document.addEventListener('contextmenu', (e) => {
  e.preventDefault()
})

// Entry-point-only component; this module is intentionally not a refresh boundary.
// eslint-disable-next-line react-refresh/only-export-components
function AppStage({ children }: { children: ReactNode }) {
  const isContentWorkbench = import.meta.env.DEV
    && typeof window !== 'undefined'
    && new URLSearchParams(window.location.search).has('dev')
  useEffect(() => {
    if (isContentWorkbench) {
      document.documentElement.style.setProperty('--app-scale', '1')
      return
    }
    function update() {
      const scale = Math.min(window.innerWidth / DESIGN_W, window.innerHeight / DESIGN_H)
      document.documentElement.style.setProperty('--app-scale', String(scale))
    }
    update()
    window.addEventListener('resize', update)
    return () => window.removeEventListener('resize', update)
  }, [isContentWorkbench])
  return <div className={`app-stage${isContentWorkbench ? ' app-stage--workbench' : ''}`}>{children}</div>
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <AccountProvider>
      <AudioProvider>
        <PresentationProvider>
          <TooltipHost>
            <AppStage>
              <App />
            </AppStage>
          </TooltipHost>
        </PresentationProvider>
      </AudioProvider>
    </AccountProvider>
  </StrictMode>,
)
