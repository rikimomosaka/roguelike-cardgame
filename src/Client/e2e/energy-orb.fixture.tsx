/* eslint-disable react-refresh/only-export-components -- Browser-only E2E fixture exports its imperative mount entry point. */
import { createRoot } from 'react-dom/client'
import { TooltipHost } from '../src/components/Tooltip'
import { EnergyOrb } from '../src/screens/BattleScreen'

function EnergyCase({ current }: { current: number }) {
  return (
    <section
      data-energy-case={current}
      style={{ position: 'relative', width: 64, height: 64 }}
    >
      <EnergyOrb cur={current} recoveryPerTurn={3} />
    </section>
  )
}

/** Mount energy boundaries without adding a production route. */
export async function mountEnergyOrbFixture() {
  document.getElementById('energy-orb-fixture')?.remove()

  const host = document.createElement('div')
  host.id = 'energy-orb-fixture'
  Object.assign(host.style, {
    position: 'fixed',
    inset: '0',
    zIndex: '2147483000',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    gap: '32px',
    background: '#111',
  })
  document.body.append(host)

  createRoot(host).render(
    <TooltipHost>
      <EnergyCase current={9} />
      <EnergyCase current={10} />
      <EnergyCase current={100} />
    </TooltipHost>,
  )

  await new Promise<void>(resolve => {
    requestAnimationFrame(() => requestAnimationFrame(() => resolve()))
  })

  host.querySelectorAll<HTMLElement>('.energy').forEach(orb => {
    orb.style.left = '0'
    orb.style.bottom = '0'
  })

  await document.fonts.ready
  document.documentElement.dataset.energyOrbReady = 'true'
}
