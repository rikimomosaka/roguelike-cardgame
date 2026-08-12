import { createRoot } from 'react-dom/client'
import { TooltipHost } from '../src/components/Tooltip'
import { BattleComboControls } from '../src/screens/BattleScreen'

type ComboControlsFixtureOptions = {
  comboGauge?: number
  tacticalDisabled?: boolean
  ultimateDisabled?: boolean
}

/** Mounts the controls in the same 1200x675 logical stage used at 1280x720. */
export async function mountComboControlsFixture(options: ComboControlsFixtureOptions = {}) {
  document.getElementById('combo-controls-fixture')?.remove()

  const host = document.createElement('div')
  host.id = 'combo-controls-fixture'
  Object.assign(host.style, {
    position: 'fixed',
    inset: '0',
    zIndex: '2147483000',
    display: 'flex',
    alignItems: 'flex-start',
    justifyContent: 'center',
    background: '#17100b',
  })
  document.body.append(host)

  createRoot(host).render(
    <TooltipHost>
      <div
        className="battle__stage"
        data-combo-layout-stage="true"
        style={{ flex: 'none', width: 1200, height: 675 }}
      >
        <BattleComboControls
          count={4}
          maxComboThisTurn={6}
          lastPlayedOrigCost={2}
          nextCardComboFreePass={false}
          comboGauge={options.comboGauge ?? 0}
          tacticalDisabled={options.tacticalDisabled ?? true}
          ultimateDisabled={options.ultimateDisabled ?? true}
          onUseTactical={() => {}}
          onUseUltimate={() => {}}
        />
        <div className="hand-wrap" data-layout-obstacle="hand" />
        <button type="button" className="end-turn" data-layout-obstacle="end-turn">
          TURN END
        </button>
      </div>
    </TooltipHost>,
  )

  await new Promise<void>(resolve => {
    requestAnimationFrame(() => requestAnimationFrame(() => resolve()))
  })
  await document.fonts.ready
  document.documentElement.dataset.comboControlsReady = 'true'
}
