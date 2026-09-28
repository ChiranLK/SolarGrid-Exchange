import type { OperatingSchedule } from './stationTypes'

interface Props {
  schedule: OperatingSchedule[]
  onChange: (schedule: OperatingSchedule[]) => void
}

export function ScheduleFields({ schedule, onChange }: Props) {
  function updateDay(index: number, changes: Partial<OperatingSchedule>) {
    onChange(schedule.map((item, position) => position === index ? { ...item, ...changes } : item))
  }

  return (
    <>
      <h2 className="h5 mt-4">Operating schedule</h2>
      <p className="text-body-secondary small">Times use the station operating timezone, Asia/Colombo (24-hour HH:mm). Closed days do not need times.</p>
      <div className="row g-3">
        {schedule.map((item, index) => (
          <div className="col-12 col-lg-6" key={item.dayOfWeek}>
            <div className="border rounded p-3 h-100">
              <div className="form-check mb-2"><input className="form-check-input" type="checkbox" id={`open-${item.dayOfWeek}`} checked={item.isOpen} onChange={(event) => updateDay(index, { isOpen: event.target.checked })} /><label className="form-check-label fw-semibold" htmlFor={`open-${item.dayOfWeek}`}>{item.dayOfWeek} open</label></div>
              {item.isOpen && <div className="row g-2">
                <div className="col-6"><label className="form-label" htmlFor={`start-${item.dayOfWeek}`}>Opens</label><input className="form-control" id={`start-${item.dayOfWeek}`} type="time" required value={item.openingTime ?? ''} onChange={(event) => updateDay(index, { openingTime: event.target.value })} /></div>
                <div className="col-6"><label className="form-label" htmlFor={`end-${item.dayOfWeek}`}>Closes</label><input className="form-control" id={`end-${item.dayOfWeek}`} type="time" required value={item.closingTime ?? ''} onChange={(event) => updateDay(index, { closingTime: event.target.value })} /></div>
              </div>}
            </div>
          </div>
        ))}
      </div>
    </>
  )
}
