"""Météo Open-Meteo : requête en tâche de fond et choix de la ville."""
from __future__ import annotations
import json
import threading
import urllib.parse
import urllib.request
import time
from app_state import CONFIG
from atomicfile import write_text as atomic_write_text
from PySide6.QtCore import QCoreApplication, Slot
from PySide6.QtWidgets import QInputDialog, QLineEdit


class WeatherMixin:
    """Météo Open-Meteo : requête en tâche de fond et choix de la ville."""

    @Slot()
    def refreshWeather(self):
        if self.weather_busy or time.monotonic() - self.weather_at < 900:
            return
        self.weather_busy = True

        def fetch():
            try:

                def read(url):
                    with urllib.request.urlopen(url, timeout=8) as response:
                        return json.load(response)

                city = self.city
                if not city:
                    geo = read('https://wttr.in/?format=j1')['nearest_area'][0]
                    city = geo['areaName'][0]['value']
                results = read(
                    'https://geocoding-api.open-meteo.com/v1/search?'
                    + urllib.parse.urlencode({'name': city, 'count': 1, 'language': 'en'})
                ).get('results', [])
                if not results:
                    raise ValueError('Ville introuvable')
                location = results[0]
                data = read(
                    'https://api.open-meteo.com/v1/forecast?'
                    + urllib.parse.urlencode(
                        {
                            'latitude': location['latitude'],
                            'longitude': location['longitude'],
                            'current': 'temperature_2m,weather_code',
                            'timezone': 'auto',
                        }
                    )
                )['current']
                code = data['weather_code']
                icon = (
                    'sunny'
                    if code == 0
                    else 'partly_cloudy_day'
                    if code < 3
                    else 'cloud'
                    if code == 3
                    else 'foggy'
                    if code < 50
                    else 'rainy'
                    if code < 70
                    else 'weather_snowy'
                    if code < 80
                    else 'thunderstorm'
                    if code >= 95
                    else 'rainy'
                )
                self.weatherResult.emit(
                    {'temperature': f"{round(data['temperature_2m'])}°C", 'description': location['name'], 'icon': icon}
                )
            except Exception:
                self.weatherResult.emit({'description': 'Météo indisponible · réessayer'})

        threading.Thread(target=fetch, daemon=True).start()

    @Slot(dict)
    def weather_ready(self, value):
        self.weather_busy = False
        self.weather_at = time.monotonic()
        self._weather = value
        self.changed.emit()

    @Slot()
    def chooseCity(self):
        self.modal = True
        try:
            city, ok = QInputDialog.getText(
                None,
                QCoreApplication.translate('', 'Météo · Open-Meteo'),
                QCoreApplication.translate('', 'Ville, pays'),
                QLineEdit.Normal,
                self.city,
            )
            if ok and city.strip():
                self.city = city.strip()[:120]
                atomic_write_text(CONFIG / 'weather.json', json.dumps({'city': self.city}))
                self.weather_at = 0
                self.refreshWeather()
        finally:
            self.modal = False
