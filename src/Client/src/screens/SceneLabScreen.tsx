// 開発者用: 背景と演出（AmbientScene）の確認画面。?dev=scenes で開く（DEV 環境のみ）。
// 一覧は ambientScenes.ts の定義から作るので、背景・場面を足せば自動で並ぶ。
import { useEffect, useMemo, useState } from 'react'
import { AmbientScene } from '../components/ambient/AmbientScene'
import { AMBIENT_SCENES } from '../components/ambient/ambientScenes'
import './SceneLabScreen.css'

type Props = { onBack: () => void }
type MotionLevel = 'full' | 'reduced' | 'off'

const GROUPS: ReadonlyArray<{ prefix: string; label: string }> = [
  { prefix: 'battle_', label: '戦闘' },
  { prefix: 'map_', label: 'マップ' },
  { prefix: 'event_', label: 'イベント' },
]

export function SceneLabScreen({ onBack }: Props) {
  const ids = useMemo(() => Object.keys(AMBIENT_SCENES).sort(), [])
  const [scene, setScene] = useState(ids[0] ?? '')
  const [ambientOn, setAmbientOn] = useState(true)
  const [motion, setMotion] = useState<MotionLevel>('full')
  const [actualSize, setActualSize] = useState(false)

  // モーション設定は html[data-motion] で効くので、この画面の間だけ切り替えて、閉じたら戻す。
  useEffect(() => {
    const root = document.documentElement
    const previous = root.getAttribute('data-motion')
    root.setAttribute('data-motion', motion)
    return () => {
      if (previous === null) root.removeAttribute('data-motion')
      else root.setAttribute('data-motion', previous)
    }
  }, [motion])

  const def = AMBIENT_SCENES[scene]
  const others = ids.filter(id => !GROUPS.some(group => id.startsWith(group.prefix)))
  const groups = [
    ...GROUPS.map(group => ({ label: group.label, ids: ids.filter(id => id.startsWith(group.prefix)) })),
    ...(others.length > 0 ? [{ label: 'その他', ids: others }] : []),
  ]
  // 実寸 = 元の絵の 2 倍（ゲームでの表示倍率）。合わせる = 表示枠の幅に収める。
  const frameStyle = def
    ? actualSize
      ? { width: def.width * 2, height: def.height * 2 }
      : { width: '100%', aspectRatio: `${def.width} / ${def.height}` }
    : undefined

  return (
    <main className="scene-lab">
      <aside className="scene-lab__list">
        <h1>背景・演出の確認</h1>
        {groups.map(group => (
          <section key={group.label}>
            <h2>{group.label}</h2>
            <ul>
              {group.ids.map(id => (
                <li key={id}>
                  <button
                    type="button"
                    className={id === scene ? 'is-selected' : undefined}
                    onClick={() => setScene(id)}
                  >
                    {id}
                  </button>
                </li>
              ))}
            </ul>
          </section>
        ))}
        <p className="scene-lab__note">
          場面は <code>src/Client/src/components/ambient/ambientScenes.ts</code> に足すとここに並びます
          （ID: battle_* / map_act* / event_イベントID）。
        </p>
        <button type="button" className="scene-lab__back" onClick={onBack}>戻る</button>
      </aside>
      <section className="scene-lab__main">
        <div className="scene-lab__controls">
          <label>
            <input type="checkbox" checked={ambientOn} onChange={event => setAmbientOn(event.target.checked)} />
            演出を表示
          </label>
          <label>
            モーション
            <select value={motion} onChange={event => setMotion(event.target.value as MotionLevel)}>
              <option value="full">通常</option>
              <option value="reduced">減らす（周期 2 倍）</option>
              <option value="off">止める（粒なし）</option>
            </select>
          </label>
          <label>
            <input type="checkbox" checked={actualSize} onChange={event => setActualSize(event.target.checked)} />
            実寸（2 倍表示）
          </label>
        </div>
        {def ? (
          <>
            <div className="scene-lab__frame" style={frameStyle}>
              <div className="scene-lab__bg" style={{ backgroundImage: `url('${def.src}')` }} />
              {ambientOn ? <AmbientScene scene={scene} /> : null}
            </div>
            <dl className="scene-lab__info">
              <dt>場面</dt><dd>{scene}</dd>
              <dt>画像</dt><dd>{def.src}（{def.width}×{def.height}）</dd>
              <dt>光の筋</dt><dd>{def.rays?.length ?? 0}</dd>
              <dt>灯り・光</dt><dd>{def.glows?.length ?? 0}</dd>
              <dt>粒</dt><dd>{(def.particles ?? []).map(group => `${group.kind}×${group.count}`).join('、') || 'なし'}</dd>
              <dt>霧</dt><dd>{def.mist?.length ?? 0}</dd>
            </dl>
          </>
        ) : <p>場面がありません。</p>}
      </section>
    </main>
  )
}
