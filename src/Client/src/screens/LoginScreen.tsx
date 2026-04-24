import { useState } from 'react'
import { createAccount, getAccount } from '../api/accounts'
import { ApiError } from '../api/client'
import { useAccount } from '../context/AccountContext'
import './LoginScreen.css'

type Tab = 'new' | 'existing'

type Props = {
  onLoggedIn: (accountId: string) => void
}

const ID_PATTERN = /^[^/\\]{1,32}$/

function validateClientSide(id: string): string | null {
  if (!id.trim()) return 'アカウント ID を入力してください。'
  if (id.length > 32) return 'アカウント ID は 32 文字以内で入力してください。'
  if (!ID_PATTERN.test(id)) return 'アカウント ID に使用できない文字が含まれています。'
  return null
}

export function LoginScreen({ onLoggedIn }: Props) {
  const { login } = useAccount()
  const [tab, setTab] = useState<Tab>('new')
  const [id, setId] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [pending, setPending] = useState(false)

  async function handleSubmit() {
    setError(null)
    const clientError = validateClientSide(id)
    if (clientError) {
      setError(clientError)
      return
    }

    setPending(true)
    try {
      if (tab === 'new') {
        await createAccount(id)
      } else {
        await getAccount(id)
      }
      login(id)
      onLoggedIn(id)
    } catch (e) {
      if (e instanceof ApiError) {
        if (tab === 'new' && e.status === 409)
          setError('その ID はすでに使われています。')
        else if (tab === 'existing' && e.status === 404)
          setError('その ID は登録されていません。')
        else setError(`エラーが発生しました (HTTP ${e.status})`)
      } else {
        setError('ネットワークエラーが発生しました。')
      }
    } finally {
      setPending(false)
    }
  }

  return (
    <main className="login-screen">
      <div className="login-screen__pattern" aria-hidden="true" />

      <div className="login-screen__content">
        <div className="login-screen__title-block">
          <div className="login-screen__ornament-top" aria-hidden="true">✦ ✦ ✦</div>
          <h1 className="login-screen__title">ROGUELIKE</h1>
          <div className="login-screen__subtitle">CARD GAME</div>
        </div>

        <div className="login-screen__divider" aria-hidden="true" />

        <div role="tablist" className="login-screen__tabs">
          <button
            type="button"
            role="tab"
            aria-selected={tab === 'new'}
            className="login-screen__tab"
            onClick={() => { setTab('new'); setError(null) }}
          >
            新規作成
          </button>
          <button
            type="button"
            role="tab"
            aria-selected={tab === 'existing'}
            className="login-screen__tab"
            onClick={() => { setTab('existing'); setError(null) }}
          >
            既存 ID で続行
          </button>
        </div>

        <div className="login-screen__form">
          <div className="login-screen__field">
            <div className="login-screen__field-label" aria-hidden="true">
              <span className="login-screen__field-mark">▸</span>
              アカウント ID
            </div>
            <input
              type="text"
              aria-label="アカウント ID"
              className="login-screen__input"
              value={id}
              onChange={(e) => setId(e.target.value)}
              maxLength={32}
              disabled={pending}
            />
          </div>

          {error && (
            <p role="alert" className="login-screen__error">{error}</p>
          )}

          <button
            type="button"
            className="login-screen__submit"
            onClick={handleSubmit}
            disabled={pending}
          >
            <span className="login-screen__submit-mark" aria-hidden="true">▸</span>
            {tab === 'new' ? 'アカウント作成' : 'ログイン'}
          </button>
        </div>
      </div>

      <div className="login-screen__footer" aria-hidden="true">
        <span>v0.8.0 · PHASE 08</span>
        <span>{tab === 'new' ? 'NEW ACCOUNT' : 'SIGN IN'}</span>
      </div>
    </main>
  )
}
