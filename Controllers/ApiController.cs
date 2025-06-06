using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Generic;
using System;
using System.Linq;
using MatriX.API.Models;
using Microsoft.AspNetCore.Http;

namespace MatriX.API.Controllers
{
    public class ApiController : Controller
    {
        IMemoryCache memoryCache;

        public ApiController(IMemoryCache m)
        {
            memoryCache = m;
        }


        [HttpPost]
        [Route("api/users/updatedb")]
        public ActionResult UpdateUsersdb([FromBody] List<UserData> updatedUsers)
        {
            var userData = HttpContext.Features.Get<UserData>();
            if (userData == null || !userData.admin)
                return Content("not admin");

            if (updatedUsers == null)
                return BadRequest("No users provided");

            // Получаем текущую базу пользователей
            var currentUsers = AppInit.usersDb;

            // Обновляем только те поля, которые пришли в post запросе
            foreach (var updatedUser in updatedUsers)
            {
                var existingUser = currentUsers.FirstOrDefault(u => (u.domainid != null && u.domainid == updatedUser.domainid) || (u.login != null && u.login == updatedUser.login));
                if (existingUser != null)
                {
                    // Обновляем только непустые/не null поля
                    foreach (var prop in typeof(UserData).GetProperties())
                    {
                        var newValue = prop.GetValue(updatedUser);
                        if (newValue != null && !Equals(newValue, GetDefault(prop.PropertyType)))
                        {
                            prop.SetValue(existingUser, newValue);
                        }
                    }
                }
                else
                {
                    // Если пользователь не найден, добавляем его в базу
                    currentUsers.Add(updatedUser);
                }
            }

            // Сохраняем обновленную базу пользователей
            AppInit.SaveUsersDb(currentUsers);

            return Ok();
        }

        // Вспомогательный метод для получения значения по умолчанию для типа
        private static object GetDefault(Type type)
        {
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }
    }
}
