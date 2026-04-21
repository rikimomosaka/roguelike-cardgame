import { useState } from 'react'
import { createAccount, getAccount } from '../api/accounts'
import { ApiError } from '../api/client'
import { Button } from '../components/Button'
import { useAccount } from '../context/AccountContext'

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
      <h1>Roguelike Card Game</h1>
      <div role="tablist" className="login-tabs">
        <button
          role="tab"
          aria-selected={tab === 'new'}
          onClick={() => { setTab('new'); setError(null) }}
        >
          新規作成
        </button>
        <button
          role="tab"
          aria-selected={tab === 'existing'}
          onClick={() => { setTab('existing'); setError(null) }}
        >
          既存 ID で続行
        </button>
      </div>
      <label>
        アカウント ID
        <input
          type="text"
          value={id}
          onChange={(e) => setId(e.target.value)}
          maxLength={32}
          disabled={pending}
        />
      </label>
      {error && <p role="alert" className="login-error">{error}</p>}
      <Button onClick={handleSubmit} disabled={pending}>
        {tab === 'new' ? 'アカウント作成' : 'ログイン'}
      </Button>
    </main>
  )
}
