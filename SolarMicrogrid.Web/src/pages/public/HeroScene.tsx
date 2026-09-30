import { CheckIcon, ClockIcon, QrIcon } from '../../components/icons'

const panelOffsets = [36, 114, 192]

/** Decorative hero scene: sunlight reaches the panels, flows to a station and on to a home. */
export function HeroScene() {
  return (
    <div className="hero-scene" aria-hidden="true">
      <svg className="hero-scene-svg" viewBox="0 0 560 440" role="presentation" focusable="false">
        <defs>
          <radialGradient id="hs-sun" cx="50%" cy="45%" r="60%">
            <stop offset="0" stopColor="#FFE39A" />
            <stop offset="0.6" stopColor="#F4B942" />
            <stop offset="1" stopColor="#E09A1E" />
          </radialGradient>
          <radialGradient id="hs-sun-glow" cx="50%" cy="50%" r="50%">
            <stop offset="0" stopColor="#F4B942" stopOpacity="0.45" />
            <stop offset="1" stopColor="#F4B942" stopOpacity="0" />
          </radialGradient>
          <linearGradient id="hs-panel" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0" stopColor="#1F6F8B" />
            <stop offset="1" stopColor="#0E3F55" />
          </linearGradient>
          <linearGradient id="hs-shine" x1="0" y1="0" x2="1" y2="0">
            <stop offset="0" stopColor="#FFFFFF" stopOpacity="0" />
            <stop offset="0.5" stopColor="#FFFFFF" stopOpacity="0.55" />
            <stop offset="1" stopColor="#FFFFFF" stopOpacity="0" />
          </linearGradient>
          <clipPath id="hs-panels-clip">
            {panelOffsets.map((x) => (
              <polygon key={x} points={`${x},330 ${x + 70},330 ${x + 98},272 ${x + 28},272`} />
            ))}
          </clipPath>
        </defs>

        {/* Ground */}
        <ellipse className="hs-ground-shadow" cx="290" cy="360" rx="265" ry="26" />
        <path className="hs-ground" d="M18 352h524" />

        {/* Sun and light beams */}
        <circle cx="448" cy="92" r="92" fill="url(#hs-sun-glow)" />
        <g className="hs-rays">
          {Array.from({ length: 12 }, (_, index) => (
            <line
              key={index}
              x1="448"
              y1="32"
              x2="448"
              y2="44"
              transform={`rotate(${index * 30} 448 92)`}
            />
          ))}
        </g>
        <circle className="hs-sun" cx="448" cy="92" r="36" fill="url(#hs-sun)" />
        <path className="hs-beam" d="M418 124 L250 268" />
        <path className="hs-beam hs-beam-2" d="M430 132 L172 268" />
        <path className="hs-beam hs-beam-3" d="M406 116 L96 268" />

        {/* Solar panels */}
        {panelOffsets.map((x) => (
          <g key={x} className="hs-panel">
            <line className="hs-leg" x1={x + 38} y1="330" x2={x + 38} y2="352" />
            <line className="hs-leg" x1={x + 62} y1="316" x2={x + 62} y2="352" />
            <polygon
              points={`${x},330 ${x + 70},330 ${x + 98},272 ${x + 28},272`}
              fill="url(#hs-panel)"
              className="hs-panel-face"
            />
            <path
              className="hs-panel-grid"
              d={`M${x + 14} 301 H${x + 84} M${x + 23} 330 L${x + 51} 272 M${x + 47} 330 L${x + 75} 272`}
            />
          </g>
        ))}
        <g clipPath="url(#hs-panels-clip)">
          <rect className="hs-shine" x="-120" y="262" width="90" height="80" fill="url(#hs-shine)" />
        </g>

        {/* Energy lines */}
        <path className="hs-line" d="M268 312 C 290 312, 296 304, 318 304" />
        <path className="hs-flow" d="M268 312 C 290 312, 296 304, 318 304" />
        <path className="hs-line" d="M390 304 C 410 304, 418 318, 440 318" />
        <path className="hs-flow hs-flow-2" d="M390 304 C 410 304, 418 318, 440 318" />

        {/* Station */}
        <g className="hs-station">
          <rect x="318" y="262" width="72" height="90" rx="12" className="hs-station-body" />
          <rect x="330" y="276" width="48" height="28" rx="6" className="hs-station-screen" />
          <rect x="336" y="284" width="8" height="12" rx="2" className="hs-bar hs-bar-1" />
          <rect x="350" y="284" width="8" height="12" rx="2" className="hs-bar hs-bar-2" />
          <rect x="364" y="284" width="8" height="12" rx="2" className="hs-bar hs-bar-3" />
          <path className="hs-bolt" d="M357 314 348 330h8l-2 12 10-17h-8l3-11z" />
        </g>

        {/* Location pin above the station */}
        <g className="hs-pin">
          <path d="M354 206c-13 0-23 10-23 23 0 17 23 37 23 37s23-20 23-37c0-13-10-23-23-23z" className="hs-pin-body" />
          <circle cx="354" cy="229" r="8" className="hs-pin-dot" />
        </g>

        {/* Home */}
        <g className="hs-home">
          <rect x="446" y="290" width="80" height="62" rx="4" className="hs-home-body" />
          <path d="M436 294 486 252 536 294" className="hs-roof" />
          <rect x="458" y="304" width="18" height="16" rx="3" className="hs-window" />
          <rect x="496" y="304" width="18" height="16" rx="3" className="hs-window hs-window-2" />
          <rect x="476" y="326" width="20" height="26" rx="3" className="hs-door" />
        </g>
      </svg>

      <div className="hero-float hero-float-slot">
        <span className="hero-float-icon"><ClockIcon width={18} height={18} /></span>
        <span>
          <small>Next slot</small>
          <strong>Today, 10:00 to 11:00</strong>
        </span>
      </div>

      <div className="hero-float hero-float-approved">
        <span className="hero-float-icon is-gold"><QrIcon width={18} height={18} /></span>
        <span>
          <small>Kottawa Solar Hub</small>
          <strong>4.5 kWh reserved</strong>
        </span>
        <span className="hero-float-check"><CheckIcon width={14} height={14} /></span>
      </div>
    </div>
  )
}
