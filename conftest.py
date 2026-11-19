"""pytest configuration: Qt must never open a real window during tests."""
import os

os.environ['QT_QPA_PLATFORM'] = 'offscreen'
