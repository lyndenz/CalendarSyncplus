﻿#region File Header

// /******************************************************************************
//  * 
//  *      Copyright (C) Ankesh Dave 2015 All Rights Reserved. Confidential
//  * 
//  ******************************************************************************
//  * 
//  *      Project:        OutlookGoogleSyncRefresh
//  *      SubProject:     OutlookGoogleSyncRefresh.Application
//  *      Author:         Dave, Ankesh
//  *      Created On:     02-02-2015 5:56 PM
//  *      Modified On:    04-02-2015 12:39 PM
//  *      FileName:       GoogleCalendarService.cs
//  * 
//  *****************************************************************************/

#endregion

#region Imports

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Google;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Requests;
using OutlookGoogleSyncRefresh.Application.Utilities;
using OutlookGoogleSyncRefresh.Common.Log;
using OutlookGoogleSyncRefresh.Common.MetaData;
using OutlookGoogleSyncRefresh.Domain.Models;

using Calendar = OutlookGoogleSyncRefresh.Domain.Models.Calendar;

#endregion

namespace OutlookGoogleSyncRefresh.Application.Services.Google
{
    [Export(typeof(ICalendarService)), Export(typeof(IGoogleCalendarService))]
    [ExportMetadata("ServiceType", CalendarServiceType.Google)]
    public class GoogleCalendarService : IGoogleCalendarService
    {
        #region Static and Constants

        private const string dictionaryKey_CalendarId = "CalendarId";

        #endregion

        #region Fields

        private readonly ApplicationLogger _applicationLogger;
        private string calendarId;

        #endregion

        #region Constructors

        [ImportingConstructor]
        public GoogleCalendarService(IAccountAuthenticationService accountAuthenticationService,
            ApplicationLogger applicationLogger)
        {
            _applicationLogger = applicationLogger;
            AccountAuthenticationService = accountAuthenticationService;
        }

        #endregion

        #region Properties
        public string CalendarServiceName
        {
            get { return "Google"; }
        }
        public IAccountAuthenticationService AccountAuthenticationService { get; set; }

        private string CalendarId
        {
            get { return calendarId; }
            set { calendarId = value; }
        }

        #endregion

        #region Private Methods

        private Event CreateGoogleCalendarEvent(Appointment calenderAppointment, bool addDescription, bool addReminder,
            bool addAttendees)
        {
            //Create Event
            var googleEvent = new Event
            {
                Start = new EventDateTime(),
                End = new EventDateTime(),
                Summary = calenderAppointment.Subject,
                Description = addDescription ? calenderAppointment.GetDescriptionData(addAttendees) : String.Empty,
                Location = calenderAppointment.Location,
                Visibility = calenderAppointment.Privacy,
                Transparency = (calenderAppointment.BusyStatus == BusyStatusEnum.Free) ? "transparent" : "opaque",
                //Need to make recurring appointment IDs unique - append the item's date   
                ExtendedProperties =
                    new Event.ExtendedPropertiesData
                    {
                        Private =
                            new Dictionary<string, string>
                            {
                                { calenderAppointment.GetSourceEntryKey(), calenderAppointment.AppointmentId }
                            }
                    }
            };


            //Add Start/End Time
            if (calenderAppointment.AllDayEvent)
            {
                if (calenderAppointment.StartTime.HasValue)
                {
                    googleEvent.Start.Date = calenderAppointment.StartTime.Value.ToString("yyyy-MM-dd");
                }
                if (calenderAppointment.EndTime.HasValue)
                {
                    googleEvent.End.Date = calenderAppointment.EndTime.Value.ToString("yyyy-MM-dd");
                }
            }
            else
            {
                googleEvent.Start.DateTimeRaw = calenderAppointment.Rfc339FormatStartTime;
                googleEvent.End.DateTimeRaw = calenderAppointment.Rfc339FormatEndTime;
            }

            //Add Reminder
            if (addReminder && calenderAppointment.ReminderSet)
            {
                googleEvent.Reminders = new Event.RemindersData
                {
                    UseDefault = false,
                    Overrides = new List<EventReminder>
                    {
                        new EventReminder
                        {
                            Method = "popup",
                            Minutes = calenderAppointment.ReminderMinutesBeforeStart
                        }
                    }
                };
            }

            //Add Required Attendees

            AddEventAttendees(calenderAppointment.RequiredAttendees, googleEvent, false);

            AddEventAttendees(calenderAppointment.OptionalAttendees, googleEvent, true);

            //Add Organizer
            if (IsValidEmailAddress(calenderAppointment.Organizer.Email))
            {
                googleEvent.Organizer = new Event.OrganizerData
                {
                    DisplayName = calenderAppointment.Organizer.Name,
                    Email = calenderAppointment.Organizer.Email
                };
            }
            return googleEvent;
        }

        private void AddEventAttendees(IEnumerable<Recipient> recipients, Event googleEvent, bool optional)
        {
            IEnumerable<Recipient> recipeintList = recipients as IList<Recipient> ?? recipients.ToList();
            if (!recipeintList.Any() && googleEvent==null)
            {
                return;
            }

            foreach (Recipient recipient in recipeintList)
            {
                //Ignore recipients with invalid Email
                if (!IsValidEmailAddress(recipient.Email))
                {
                    continue;
                }
                var eventAttendee = new EventAttendee()
                {
                    DisplayName = recipient.Name,
                    Email = recipient.Email,
                    Optional = optional
                };
                googleEvent.Attendees.Add(eventAttendee);
            }

        }

        private bool IsValidEmailAddress(string email)
        {
            var emailRegex = @"^[\w!#$%&'*+\-/=?\^_`{|}~]+(\.[\w!#$%&'*+\-/=?\^_`{|}~]+)*" + "@" +
                             @"((([\-\w]+\.)+[a-zA-Z]{2,4})|(([0-9]{1,3}\.){3}[0-9]{1,3}))$";

            return Regex.IsMatch(email, emailRegex);
        }

        private CalendarService GetCalendarService()
        {
            return AccountAuthenticationService.AuthenticateCalenderOauth();
        }

        private void InsertEventErrorMessage(Event content, RequestError error, int index, HttpResponseMessage message,
            Dictionary<KeyValuePair<int, Appointment>, HttpResponseMessage> eventListIndex)
        {
            //var getKey = eventListIndex.FirstOrDefault(pair => pair.Key.Key == index);
            // eventListIndex[getKey.Key] = message;
        }

        private Appointment CreateAppointment(Event googleEvent)
        {
            Appointment appointment;
            
            if (googleEvent.Start.DateTime == null && googleEvent.End.DateTime == null)
            {
                appointment = new Appointment(googleEvent.Description, googleEvent.Location, googleEvent.Summary,
                    DateTime.Parse(googleEvent.End.Date),
                    DateTime.Parse(googleEvent.Start.Date), googleEvent.Id) { AllDayEvent = true };
            }
            else
            {
                appointment = new Appointment(googleEvent.Description, googleEvent.Location, googleEvent.Summary,
                    googleEvent.End.DateTime,
                    googleEvent.Start.DateTime, googleEvent.Id);
            }

            if (googleEvent.RecurringEventId != null && !string.IsNullOrEmpty(googleEvent.RecurringEventId))
            {
                appointment.IsRecurring = true;
            }
            appointment.CalendarId = CalendarId;
            if (googleEvent.ExtendedProperties != null && googleEvent.ExtendedProperties.Private != null)
            {
                foreach (var property in googleEvent.ExtendedProperties.Private)
                {
                    appointment.ExtendedProperties.Add(property.Key, property.Value);
                }
            }

            //Add Organizer
            appointment.Organizer = new Recipient()
            {
                Name = googleEvent.Organizer.DisplayName,
                Email = googleEvent.Organizer.Email
            };

            //Add Required Attendee
            AddAttendee(googleEvent, appointment,false);

            //Add optional Attendee
            AddAttendee(googleEvent, appointment, true);


            return appointment;
        }

        private static void AddAttendee(Event googleEvent, Appointment appointment,bool isOptional)
        {
            var list = isOptional ? appointment.OptionalAttendees : appointment.RequiredAttendees;
            if (!list.Any())
            {
                return;
            }
            foreach (EventAttendee eventAttendee in googleEvent.Attendees.Where(attendee => attendee.Optional.GetValueOrDefault()==isOptional))
            {
                list.Add(new Recipient() { Name = eventAttendee.DisplayName, Email = eventAttendee.Email });
            }
        }

        #endregion

        #region IGoogleCalendarService Members

        public void CheckCalendarSpecificData(IDictionary<string, object> calendarSpecificData)
        {
            if (calendarSpecificData == null)
            {
                throw new ArgumentNullException("calendarSpecificData", "Calendar Specific Data cannot be null");
            }

            object keyValue;
            if (!calendarSpecificData.TryGetValue(dictionaryKey_CalendarId, out keyValue))
            {
                throw new InvalidOperationException(string.Format("{0} is a required.", dictionaryKey_CalendarId));
            }
            else
            {
                CalendarId = keyValue as string;

                if (string.IsNullOrEmpty(calendarId))
                {
                    throw new InvalidOperationException(string.Format("{0} cannot be null or empty.", dictionaryKey_CalendarId));
                }
            }
        }

        public async Task<List<Calendar>> GetAvailableCalendars(IDictionary<string, object> calendarSpecificData)
        {
            //Get Calendar Service
            CalendarService calendarService = GetCalendarService();

            CalendarList calenderList = await calendarService.CalendarList.List().ExecuteAsync();

            List<Calendar> locaCalendarList =
                calenderList.Items.Select(
                    calendarListEntry => new Calendar { Id = calendarListEntry.Id, Name = calendarListEntry.Summary })
                    .ToList();
            return locaCalendarList;
        }

        public async Task<bool> AddCalendarEvent(Appointment calendarAppointment, bool addDescription,
            bool addReminder, bool addAttendees, IDictionary<string, object> calendarSpecificData)
        {
            CheckCalendarSpecificData(calendarSpecificData);

            //Get Calendar Service
            CalendarService calendarService = GetCalendarService();

            if (calendarAppointment == null || string.IsNullOrEmpty(CalendarId))
            {
                return false;
            }

            Event calendarEvent = CreateGoogleCalendarEvent(calendarAppointment, addDescription, addReminder,
                addAttendees);
            Event returnEvent = await calendarService.Events.Insert(calendarEvent, CalendarId).ExecuteAsync();

            return returnEvent != null;
        }

        public async Task<bool> AddCalendarEvent(List<Appointment> appointments, bool addDescription,
            bool addReminder, bool addAttendees, IDictionary<string, object> calendarSpecificData)
        {
            CheckCalendarSpecificData(calendarSpecificData);
            object obj;

            var eventIndexList = new Dictionary<KeyValuePair<int, Appointment>, HttpResponseMessage>();
            //Get Calendar Service
            CalendarService calendarService = GetCalendarService();

            if (appointments == null || string.IsNullOrEmpty(CalendarId))
            {
                return false;
            }
            if (appointments.Any())
            {
                //Create a Batch Request
                var batchRequest = new BatchRequest(calendarService);

                //Split the list of appointments by 1000 per list

                //Iterate over each appointment to create a event and batch it 
                for (int i = 0; i < appointments.Count; i++)
                {
                    if (i != 0 && i % 999 == 0)
                    {
                        await batchRequest.ExecuteAsync();
                        batchRequest = new BatchRequest(calendarService);
                    }

                    Appointment appointment = appointments[i];
                    Event calendarEvent = CreateGoogleCalendarEvent(appointment, addDescription, addReminder,
                        addAttendees);
                    EventsResource.InsertRequest insertRequest = calendarService.Events.Insert(calendarEvent, CalendarId);
                    insertRequest.SendNotifications = false;
                    batchRequest.Queue<Event>(insertRequest,
                        (content, error, index, message) =>
                            InsertEventErrorMessage(content, error, index, message, eventIndexList));
                }

                await batchRequest.ExecuteAsync();
            }
            return true;
        }

        public async Task<bool> DeleteCalendarEvent(Appointment calendarAppointment,
            IDictionary<string, object> calendarSpecificData)
        {
            CheckCalendarSpecificData(calendarSpecificData);

            //Get Calendar Service
            CalendarService calendarService = GetCalendarService();

            if (calendarAppointment == null || string.IsNullOrEmpty(CalendarId))
            {
                return false;
            }

            //TODO: Manage return value for Event delete
            string returnValue =
                await calendarService.Events.Delete(CalendarId, calendarAppointment.AppointmentId).ExecuteAsync();
            return true;
        }

        public async Task<bool> DeleteCalendarEvent(List<Appointment> calendarAppointment,
            IDictionary<string, object> calendarSpecificData)
        {
            CheckCalendarSpecificData(calendarSpecificData);

            var eventIndexList = new Dictionary<KeyValuePair<int, Appointment>, HttpResponseMessage>();
            //Get Calendar Service
            CalendarService calendarService = GetCalendarService();

            if (calendarAppointment == null || string.IsNullOrEmpty(CalendarId))
            {
                return false;
            }
            if (calendarAppointment.Any())
            {
                //Create a Batch Request
                var batchRequest = new BatchRequest(calendarService);

                //Split the list of appointments by 1000 per list

                //Iterate over each appointment to create a event and batch it 
                for (int i = 0; i < calendarAppointment.Count; i++)
                {
                    if (i != 0 && i % 999 == 0)
                    {
                        await batchRequest.ExecuteAsync();
                        batchRequest = new BatchRequest(calendarService);
                    }

                    Appointment appointment = calendarAppointment[i];
                    EventsResource.DeleteRequest deleteRequest = calendarService.Events.Delete(CalendarId,
                        appointment.AppointmentId);
                    batchRequest.Queue<Event>(deleteRequest,
                        (content, error, index, message) =>
                            InsertEventErrorMessage(content, error, index, message, eventIndexList));
                }

                await batchRequest.ExecuteAsync();
            }
            return true;
        }

        public async Task<CalendarAppointments> GetCalendarEventsInRangeAsync(int daysInPast, int daysInFuture,
            IDictionary<string, object> calendarSpecificData)
        {
            CheckCalendarSpecificData(calendarSpecificData);

            //Get Calendar Service
            CalendarService calendarService = GetCalendarService();

            var finalEventList = new List<Appointment>();

            Events result = null;

            EventsResource.ListRequest eventListRequest = calendarService.Events.List(CalendarId);

            // Add Filters to event List Request
            eventListRequest.TimeMin = DateTime.Now.AddDays(-(daysInPast));
            eventListRequest.TimeMax = DateTime.Now.AddDays((daysInFuture + 1));

            try
            {
                result = eventListRequest.Execute();
            }
            catch (GoogleApiException exception)
            {
                _applicationLogger.LogError(exception.ToString());
                return null;
            }
            if (result != null)
            {
                while (result.Items != null)
                {
                    // Add events to list
                    finalEventList.AddRange(
                        result.Items.Select(CreateAppointment));

                    //If all pages are over break
                    if (result.NextPageToken == null)
                    {
                        break;
                    }

                    //Set the next page to pull from request
                    eventListRequest.PageToken = result.NextPageToken;

                    result = await eventListRequest.ExecuteAsync();
                }
            }
            else
            {
                return null;
            }
            var calendarAppointments = new CalendarAppointments() { CalendarId = this.CalendarId };
            calendarAppointments.AddRange(finalEventList);
            return calendarAppointments;
        }

        #endregion
    }
}