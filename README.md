# PawCare CRM

A fresh, browser-based veterinary customer relationship and clinic operations system built around the supplied PawCare brand palette.

## Run

Open `dist/index.html` directly, or serve the folder with any static web server.

```powershell
python -m http.server 4173 --directory dist
```

Then open <http://localhost:4173>.

## Demo accounts

- Receptionist: `reception@pawcare.test`
- Manager: `manager@pawcare.test`
- Password: `pawcare123`

Demo records and newly entered data are stored in the current browser's `localStorage`. Use **Reset demo data** from the dashboard to return to the seeded dataset.

## Role coverage

Receptionist: dashboard, client profiles, appointments, inquiry data collection, reminders/follow-ups, and promotional offers.

Manager: dashboard, customer appointments, patient/client satisfaction, feedback and escalations, and reports.
