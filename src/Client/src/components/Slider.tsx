type Props = {
  label: string
  value: number
  onChange: (next: number) => void
  min?: number
  max?: number
}

export function Slider({ label, value, onChange, min = 0, max = 100 }: Props) {
  return (
    <label className="slider">
      <span className="slider__label">{label}</span>
      <input
        type="range"
        min={min}
        max={max}
        value={value}
        onChange={(e) => onChange(Number(e.target.value))}
      />
      <span className="slider__value">{value}</span>
    </label>
  )
}
