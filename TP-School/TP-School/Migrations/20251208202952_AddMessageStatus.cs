using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TP_School.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Homeworks_Schedules_LessonId",
                table: "Homeworks");

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Messages",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Homeworks",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<int>(
                name: "GradeValue",
                table: "Grades",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<string>(
                name: "Comment",
                table: "Grades",
                type: "longtext",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "longtext")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddForeignKey(
                name: "FK_Homeworks_Schedules_LessonId",
                table: "Homeworks",
                column: "LessonId",
                principalTable: "Schedules",
                principalColumn: "LessonId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Homeworks_Schedules_LessonId",
                table: "Homeworks");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Homeworks");

            migrationBuilder.AlterColumn<int>(
                name: "GradeValue",
                table: "Grades",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.UpdateData(
                table: "Grades",
                keyColumn: "Comment",
                keyValue: null,
                column: "Comment",
                value: "");

            migrationBuilder.AlterColumn<string>(
                name: "Comment",
                table: "Grades",
                type: "longtext",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "longtext",
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddForeignKey(
                name: "FK_Homeworks_Schedules_LessonId",
                table: "Homeworks",
                column: "LessonId",
                principalTable: "Schedules",
                principalColumn: "LessonId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
